namespace Model.File;

/// <summary>
/// Content staged on the server in chunks (<c>POST /Files/local/chunk</c>), waiting for an endpoint to
/// complete it into a stored file: the upload id the chunks were sent under, and how many there are.
/// </summary>
public sealed record StagedUpload(string FileId, int TotalChunks);
