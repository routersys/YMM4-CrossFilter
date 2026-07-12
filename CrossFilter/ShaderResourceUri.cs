namespace CrossFilter;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/CrossFilter;component/Shaders/{shaderName}.cso", UriKind.Absolute);
}
