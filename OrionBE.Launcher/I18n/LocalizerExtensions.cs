using System.Diagnostics.CodeAnalysis;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace OrionBE.Launcher.I18n;

public sealed class LocalizerExtensions : MarkupExtension
{
    /// <summary>Parameterless ctor required for compiled bindings when using <c>Key='…'</c> property syntax.</summary>
    public LocalizerExtensions()
    {
    }

    public LocalizerExtensions(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = "";

    /// <summary>Optional prefix; combined with <see cref="Key"/> as <c>context_key</c> (nested JSON path).</summary>
    public string Context { get; set; } = "";

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(Localizer))]
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new InvalidOperationException("LocalizerExtensions requires Key (use Key='navbar_home' with compiled bindings).");
        }

        var keyToUse = string.IsNullOrWhiteSpace(Context)
            ? Key
            : $"{Context}_{Key}";

        // Binding (não ReflectionBindingExtension) resolve correctamente o indexador em runtime + Invalidate().
        return new Binding($"[{keyToUse}]")
        {
            Mode = BindingMode.OneWay,
            Source = Localizer.Instance,
        };
    }
}
