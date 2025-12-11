namespace EDPM37.Executors;

public readonly record struct ConfirmRequest(string Text);

public readonly record struct ConfirmResult(string Text, bool IsConfirmed);