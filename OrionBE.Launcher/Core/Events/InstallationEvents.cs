namespace OrionBE.Launcher.Core.Events;

public enum InstallationLogSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Linha de registo textual da instalação (ecrã de nova instância).</summary>
public sealed record InstallationLogLine(string Message, InstallationLogSeverity Severity = InstallationLogSeverity.Info);

public sealed record InstallationProgressChanged(string Step, double Progress01);

public sealed record InstallationExtrasStep(string Message);

public sealed record ModInstallWarning(string Message);

public sealed record InstancesChanged;
