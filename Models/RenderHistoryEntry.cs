namespace LangKinhHuKhong.TTS.Models;

/// <summary>Mỗi MP3 có mã riêng, tên hiển thị/tải về và chỉ thuộc một phiên/tab.</summary>
public sealed record RenderHistoryEntry(
    string JobId,
    string Filename,
    DateTimeOffset CreatedAt,
    long FileSize,
    int CharacterCount,
    string VoiceName,
    double Speed,
    string? CustomTitle = null,
    int MergedParts = 0)
{
    public bool IsMerged => MergedParts > 0;
    public string DisplayName => CustomTitle is { Length: > 0 } ? CustomTitle + ".mp3" : Filename;
}
