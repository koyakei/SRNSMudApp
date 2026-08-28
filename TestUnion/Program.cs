using System;
using System.Text.Json;

namespace TestUnion;

public record ItemTarget(int TargetItemId);
public record TagTarget(int TargetTagId);
public readonly union TimelineTarget(ItemTarget, TagTarget);

class Program
{
    static void Main()
    {
        try
        {
            TimelineTarget target = new ItemTarget(123);
            string json = JsonSerializer.Serialize(target);
            Console.WriteLine("Serialized: " + json);

            var deserialized = JsonSerializer.Deserialize<TimelineTarget>(json);
            Console.WriteLine("Deserialized type: " + deserialized.GetType());
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception: " + ex);
        }
    }
}

[System.Runtime.CompilerServices.Union]
public struct Shape : System.Runtime.CompilerServices.IUnion
{
    private readonly object? _value;

    public Shape(Circle value) { _value = value; }
    public Shape(Rectangle value) { _value = value; }

    public object? Value => _value;
}

public record class Circle(double Radius);
public record class Rectangle(double Width, double Height);

static void ManualUnionExample()
{
    Shape shape = new Shape(new Circle(5.0));

    var area = shape switch
    {
        Circle c => Math.PI * c.Radius * c.Radius,
        Rectangle r => r.Width * r.Height,
    };
    Console.WriteLine($"{area:F2}"); // output: 78.54
}