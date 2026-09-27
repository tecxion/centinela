namespace Centinela.App;

public enum ToastStyle { Error, Recovery, Info }

/// <summary>A notice; with <see cref="OnAction"/>, clicking it runs the action instead of opening the error log.</summary>
public sealed record Toast(string Title, string Message, ToastStyle Style, string? ActionText = null, Action? OnAction = null);