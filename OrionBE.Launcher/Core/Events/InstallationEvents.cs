namespace OrionBE.Launcher.Core.Events;

public sealed record InstallationProgressChanged(string Step, double Progress01);

public sealed record InstallationExtrasStep(string Message);

public sealed record ModInstallWarning(string Message);

public sealed record InstancesChanged;
