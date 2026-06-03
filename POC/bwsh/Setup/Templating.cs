using System.Reflection;
using HandlebarsDotNet;

namespace Bit.SelfHost.Setup;

/// <summary>Loads the embedded Setup .hbs templates and compiles them (port of Helpers.ReadTemplate).</summary>
public static class Templating
{
    public static HandlebarsTemplate<object, object> Read(string templateName)
    {
        var assembly = typeof(Templating).GetTypeInfo().Assembly;
        var resource = $"Bit.SelfHost.Setup.Templates.{templateName}.hbs";
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded template not found: {resource}");
        using var reader = new StreamReader(stream);
        return Handlebars.Compile(reader.ReadToEnd());
    }
}
