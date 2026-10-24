using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

using Microsoft.Data.SqlClient;

namespace SRNSMudApp.E2ETests;

/// <summary>
///     ネットワーク名前空間が分離された環境（サンドボックス等）でホスト側の Docker マップポートへ
///     直接 TCP 接続できない場合に、<c>127.0.0.1:&lt;localPort&gt;</c> を受け付けて
///     <c>docker exec -i</c> 経由でコンテナ内の <c>127.0.0.1:1433</c> へ双方向中継するプロキシ。
///     ホスト側ポートへ直接 TCP 接続できる通常環境ではプロキシを起動せず元の接続文字列をそのまま返す。
/// </summary>
[SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "テスト終了時やソケット切断時の非同期ストリーム例外を安全に無視するため")]
public sealed class DockerSocketSqlProxy : IAsyncDisposable
{
    private const string PythonBridgeScript = """
        import os, socket, sys, threading
        s = socket.create_connection(('127.0.0.1', 1433))
        def p():
            try:
                while True:
                    d = os.read(0, 65536)
                    if not d: break
                    s.sendall(d)
            except Exception: pass
            finally:
                try: s.shutdown(socket.SHUT_WR)
                except Exception: pass
        threading.Thread(target=p, daemon=True).start()
        try:
            out = sys.stdout.buffer
            while True:
                d = s.recv(65536)
                if not d: break
                out.write(d)
                out.flush()
        except Exception: pass
        finally:
            s.close()
        """;

    private readonly string _containerId;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoopTask;

    private DockerSocketSqlProxy(string containerId, string connectionString, TcpListener listener)
    {
        _containerId = containerId;
        ConnectionString = connectionString;
        _listener = listener;
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>
    ///     クライアントが使用する SQL Server 接続文字列。
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    ///     コンテナの接続文字列へ直接 TCP 接続できるか確認し、接続できない場合はローカルプロキシを起動して
    ///     書き換えた接続文字列を返す。直接接続できる場合はプロキシを作らず元の接続文字列を返す。
    /// </summary>
    public static async Task<(string ConnectionString, DockerSocketSqlProxy? Proxy)> EnsureReachableAsync(
        string containerId,
        string containerConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerConnectionString);

        var builder = new SqlConnectionStringBuilder(containerConnectionString);
        if (await CanConnectDirectlyAsync(builder.DataSource).ConfigureAwait(false))
        {
            return (containerConnectionString, null);
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int localPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        builder.DataSource = $"127.0.0.1,{localPort}";
        var proxy = new DockerSocketSqlProxy(containerId, builder.ConnectionString, listener);
        return (proxy.ConnectionString, proxy);
    }

    private static async Task<bool> CanConnectDirectlyAsync(string dataSource)
    {
        try
        {
            string host = "127.0.0.1";
            int port = 1433;
            int commaIndex = dataSource.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex >= 0)
            {
                host = dataSource[..commaIndex].Trim();
                _ = int.TryParse(dataSource[(commaIndex + 1)..].Trim(), out port);
            }

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                host = "127.0.0.1";
            }

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
            }
        }
        catch
        {
            // リスナー停止時は正常終了
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            var psi = new ProcessStartInfo("docker")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("exec");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(_containerId);
            psi.ArgumentList.Add("python3");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(PythonBridgeScript);

            using Process? proc = Process.Start(psi);
            if (proc is null)
            {
                return;
            }

            try
            {
                await using NetworkStream netStream = client.GetStream();
                Stream dockerIn = proc.StandardInput.BaseStream;
                Stream dockerOut = proc.StandardOutput.BaseStream;

                Task toContainer = Task.Run(async () =>
                {
                    byte[] buffer = new byte[65536];
                    try
                    {
                        int read;
                        while ((read = await netStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                        {
                            await dockerIn.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            await dockerIn.FlushAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        // 切断時は無視
                    }
                    finally
                    {
                        try
                        {
                            dockerIn.Close();
                        }
                        catch
                        {
                        }
                    }
                }, CancellationToken.None);

                Task fromContainer = Task.Run(async () =>
                {
                    byte[] buffer = new byte[65536];
                    try
                    {
                        int read;
                        while ((read = await dockerOut.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                        {
                            await netStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            await netStream.FlushAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        // 切断時は無視
                    }
                    finally
                    {
                        try
                        {
                            client.Client.Shutdown(SocketShutdown.Send);
                        }
                        catch
                        {
                        }
                    }
                }, CancellationToken.None);

                await Task.WhenAll(toContainer, fromContainer).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            _listener.Stop();
        }
        catch
        {
        }

        try
        {
            await _acceptLoopTask.ConfigureAwait(false);
        }
        catch
        {
        }

        _cts.Dispose();
    }
}