namespace Centinela.Desktop.Services.Info;

/// <summary>One block of a read-only document (manual, licence, support) shown by the info window.</summary>
abstract record InfoBlock;

sealed record InfoHeading(string Text) : InfoBlock;

/// <summary>A paragraph; <paramref name="Lead"/>, when present, goes in bold before the text.</summary>
sealed record InfoParagraph(string Text, string? Lead = null) : InfoBlock;

sealed record InfoLink(string Text, string Uri) : InfoBlock;

/// <summary>Preformatted text in a monospace font.</summary>
sealed record InfoCode(string Text) : InfoBlock;
