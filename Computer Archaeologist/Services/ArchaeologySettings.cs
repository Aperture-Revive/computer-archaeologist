using ComputerArchaeologist.Core.Options;

namespace ComputerArchaeologist.Services;

/// <summary>
/// Groups the option objects a page needs into one injectable value, so view models do not have to
/// declare several separate option dependencies.
/// </summary>
public sealed class ArchaeologySettings
{
    public ArchaeologySettings(DiscoveryOptions discovery, ArchaeologyOptions archaeology, OpenAiOptions openAi)
    {
        Discovery = discovery;
        Archaeology = archaeology;
        OpenAi = openAi;
    }

    public DiscoveryOptions Discovery { get; }

    public ArchaeologyOptions Archaeology { get; }

    public OpenAiOptions OpenAi { get; }
}
