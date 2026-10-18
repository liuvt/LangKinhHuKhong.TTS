using LangKinhHuKhong.TTS.Components;
using LangKinhHuKhong.TTS.Services;

var builder = WebApplication.CreateBuilder(args);
// 100.000 ký tự Unicode qua Blazor JS interop cần nhiều hơn 32KiB mặc định.
// Text vẫn nằm tại trình duyệt cho tới khi người dùng bấm Render.
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 2 * 1024 * 1024);
builder.Services.AddSingleton<VoiceService>();
builder.Services.AddSingleton<TextSplitterService>();
builder.Services.AddSingleton<NativePathService>();
builder.Services.AddSingleton<TtsEngineService>();
builder.Services.AddSingleton<AudioMergeService>();
builder.Services.AddSingleton<SessionOutputService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionOutputService>());
builder.Services.AddSingleton<RenderService>();
builder.Services.AddSingleton<PlaylistMergeService>();

var app = builder.Build();
foreach (var path in new[] { "Temp", "Output", "Voices" })
    Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, path));

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Không cho đọc MP3 nếu thiếu session secret: mỗi tab/user hoàn toàn tách biệt.
app.MapGet("/download/{sessionId}/{file}", (string sessionId, string file, string? key,
    SessionOutputService outputs, HttpContext http) =>
{
    var downloadName = outputs.DownloadName(sessionId, file, key);
    if (downloadName is null) return Results.NotFound();
    var path = outputs.OutputPath(sessionId, file);
    if (!File.Exists(path)) return Results.NotFound();
    http.Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
    http.Response.Headers["X-Content-Type-Options"] = "nosniff";
    http.Response.Headers["Referrer-Policy"] = "no-referrer";
    return Results.File(path, "audio/mpeg", fileDownloadName: downloadName, enableRangeProcessing: true);
});

// Phát trực tiếp bằng <audio>, hỗ trợ tua/range, không gắn Content-Disposition attachment.
app.MapGet("/listen/{sessionId}/{file}", (string sessionId, string file, string? key,
    SessionOutputService outputs, HttpContext http) =>
{
    if (!outputs.CanDownload(sessionId, file, key)) return Results.NotFound();
    var path = outputs.OutputPath(sessionId, file);
    if (!File.Exists(path)) return Results.NotFound();
    http.Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
    http.Response.Headers["X-Content-Type-Options"] = "nosniff";
    http.Response.Headers["Referrer-Policy"] = "no-referrer";
    return Results.File(path, "audio/mpeg", enableRangeProcessing: true);
});

app.MapPost("/api/session/{sessionId}/touch", (string sessionId, string? key,
    SessionOutputService outputs) =>
    outputs.Touch(sessionId, key) ? Results.NoContent() : Results.NotFound());

// pagehide chỉ báo "leave": F5 có 12 giây tiếp tục phiên cũ;
// đóng tab thực sự -> xóa ngay sau khoảng chờ (tối đa chu kỳ quét 5 giây).
app.MapPost("/api/session/{sessionId}/leave", (string sessionId, string? key,
    SessionOutputService outputs) =>
    outputs.Leave(sessionId, key) ? Results.NoContent() : Results.NotFound());

// API close dành cho thao tác kết thúc phiên có xác thực.
app.MapPost("/api/session/{sessionId}/close", (string sessionId, string? key,
    SessionOutputService outputs) =>
    outputs.Close(sessionId, key) ? Results.NoContent() : Results.NotFound());

app.Run();
