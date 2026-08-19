using System;
using System.Text.Json;

namespace TestUnion;

public record ItemTarget(int TargetItemId);
public record TagTarget(int TargetTagId);
public union TimelineTarget(ItemTarget, TagTarget);

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
