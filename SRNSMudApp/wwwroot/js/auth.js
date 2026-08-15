window.customAuth = {
    async loginWithToken(provider, token) {
        console.log(`[AUTH JS] loginWithToken called with provider=${provider}, token=${token}`);
        try {
            const response = await fetch('/api/auth/external-login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    provider,
                    token
                })
            });

            if (response.ok) {
                // Reload the page to reflect the new authenticated state
                window.location.href = '/';
            } else {
                const errorText = await response.text();
                alert('Authentication failed: ' + errorText);
            }
        } catch (error) {
            console.error('Login error:', error);
            alert('An error occurred during authentication.');
        }
    },

    // Programmatic render of Google Sign-in button
    renderGoogleButton(clientId, containerId) {
        if (typeof google === 'undefined' || !google.accounts || !google.accounts.id) {
            console.warn("Google Identity Services is not loaded yet. Retrying in 100ms...");
            setTimeout(() => window.customAuth.renderGoogleButton(clientId, containerId), 100);
            return;
        }

        try {
            google.accounts.id.initialize({
                client_id: clientId,
                callback(response) {
                    window.customAuth.loginWithToken('Google', response.credential);
                }
            });

            google.accounts.id.renderButton(
                document.getElementById(containerId),
                {theme: "outline", size: "large", type: "standard", shape: "rectangular", width: "100%"}
            );
        } catch (e) {
            console.error("Error rendering Google button:", e);
        }
    },

    redirectToLine(clientId, redirectUri) {
        const state = Math.random().toString(36).substring(2);
        const url = `https://access.line.me/oauth2/v2.1/authorize?response_type=code&client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&state=${state}&scope=profile%20openid`;
        window.location.href = url;
    },

    redirectToGithub(clientId, redirectUri) {
        const url = `https://github.com/login/oauth/authorize?client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&scope=read:user,user:email`;
        window.location.href = url;
    }
};
