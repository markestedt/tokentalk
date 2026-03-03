using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TokenTalk;
using TokenTalk.Audio;
using TokenTalk.Configuration;
using TokenTalk.Platform;
using TokenTalk.PostProcessing;
using TokenTalk.Storage;
using TokenTalk.Transcription;
using TokenTalk.Overlay;
using TokenTalk.Tray;
using TokenTalk.UI;
using TokenTalk.UI.ViewModels;

namespace TokenTalk;

public class Program
{
    [STAThread]
    public static void Main()
    {
        var cts = new CancellationTokenSource();

        // ── Logging ──────────────────────────────────────────────────────
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(LogLevel.Information)
                .AddSimpleConsole(opts =>
                {
                    opts.TimestampFormat = "HH:mm:ss ";
                    opts.SingleLine = true;
                });
        });

        var logger = loggerFactory.CreateLogger<Program>();

        // ── Configuration ─────────────────────────────────────────────────
        var configPath = ConfigManager.GetConfigPath();
        var configDir = ConfigManager.GetConfigDirectory();
        Directory.CreateDirectory(configDir);

        var configManager = new ConfigManager(configPath, loggerFactory.CreateLogger<ConfigManager>());
        var cfg = configManager.Current;

        logger.LogInformation("TokenTalk starting. Config: {Path}", configPath);

        // ── Database ──────────────────────────────────────────────────────
        var dbPath = Path.Combine(configDir, "tokentalk.db");
        var db = new TokenTalkDbContext(dbPath);
        db.InitializeAsync().GetAwaiter().GetResult();
        var repository = new DictationRepository(db);

        // ── Dictionary ────────────────────────────────────────────────────
        var dictionaryService = new DictionaryService(loggerFactory.CreateLogger<DictionaryService>());
        var dictionary = dictionaryService.Load(cfg.PostProcessing.DictionaryFile);

        // ── HTTP Client Factory ───────────────────────────────────────────
        IHttpClientFactory httpClientFactory = new SimpleHttpClientFactory(TimeSpan.FromSeconds(60));

        // ── Model Manager (whisper.cpp local models) ──────────────────────
        var modelsDir = Path.Combine(configDir, "models");
        var modelManager = new ModelManager(modelsDir);

        // ── Transcription Provider ────────────────────────────────────────
        string BuildWhisperPrompt(TokenTalkOptions options)
        {
            var sb = new System.Text.StringBuilder(
                "Transcribe accurately with correct grammar, punctuation, and capitalization. " +
                "Sentences start with a capital letter and end with a period, question mark, or exclamation mark. " +
                "Remove filler words (um, uh, like, you know, I mean) unless they carry meaning. " +
                "Use numerals for specific quantities (e.g., 'five items' → '5 items', 'thirty percent' → '30%'). " +
                "Preserve proper nouns and brand names with their correct capitalisation. " +
                "Treat spoken punctuation commands as formatting: 'comma' → ',', 'period' or 'full stop' → '.', 'new line' → line break, 'new paragraph' → paragraph break, 'open quote'/'close quote' → quotation marks. " +
                "Correct minor grammatical errors while preserving the speaker's intended meaning, voice, and tone. " +
                "Do not add commentary, explanations, or any text that was not spoken. ");

            if (options.DeveloperMode)
                sb.Append(
                    "Developer mode: transcribe all technical content precisely. " +
                    "Recognise programming languages: C#, F#, VB.NET, Python, JavaScript, TypeScript, Rust, Go, Java, Kotlin, Swift, C, C++, PHP, Ruby. " +
                    "Recognise frameworks and libraries: .NET, ASP.NET Core, Entity Framework, LINQ, WPF, WinForms, React, Vue, Angular, Next.js, Node.js, Express, FastAPI, Django, Spring Boot. " +
                    "Recognise cloud and infrastructure terms: Azure, AWS, GCP, Kubernetes, Docker, Terraform, Helm, CI/CD, GitHub Actions, Azure DevOps, Bicep, ARM. " +
                    "Recognise developer tools: Visual Studio, VS Code, JetBrains Rider, Git, GitHub, GitLab, npm, pnpm, NuGet, pip, cargo, Postman. " +
                    "Expand acronyms correctly: API, REST, GraphQL, gRPC, SQL, NoSQL, JSON, XML, YAML, HTML, CSS, JWT, OAuth, OIDC, CRUD, ORM, DI, IoC, MVVM, MVC, SPA, PWA, SDK, CLI, IDE, TDD, BDD, DDD, CQRS, SOLID. " +
                    "Preserve identifier casing: camelCase for variables and methods, PascalCase for classes and types, snake_case or SCREAMING_SNAKE_CASE as spoken. " +
                    "Recognise spoken code constructs: 'async await', 'try catch finally', 'if else', 'for loop', 'foreach', 'lambda', 'dependency injection', 'interface', 'abstract class', 'generic type', 'null check', 'null coalescing'. ");

            sb.Append("Format output as natural, well-structured text in the configured language.");

            if (!string.IsNullOrEmpty(options.Transcription.Prompt))
                sb.Append(' ').Append(options.Transcription.Prompt);
            return sb.ToString();
        }

        ITranscriptionProvider transcriptionProvider = new TranscriptionProviderFactory(
            () => configManager.Current.Transcription.Provider,
            new OpenAiWhisperProvider(
                httpClientFactory,
                () => configManager.Current.Transcription.ApiKey,
                () => configManager.Current.Transcription.Model,
                () => configManager.Current.Transcription.Language,
                () => BuildWhisperPrompt(configManager.Current),
                dictionary.GetSimpleTerms()),
            new WhisperCppProvider(
                () => configManager.Current.Transcription.ModelPath,
                () => configManager.Current.Transcription.Language));

        // ── Post-Processing Pipeline ──────────────────────────────────────
        var pipeline = new PostProcessingPipeline(loggerFactory.CreateLogger<PostProcessingPipeline>());

        // Dictionary mapping replacement always runs when entries exist (independent of PostProcessing toggle)
        if (dictionary.Entries.Any(e => e.IsMapping))
            pipeline.AddProcessor(new DictionaryProcessor(dictionary));

        pipeline.AddProcessor(new VoiceCommandProcessor(() => configManager.Current.PostProcessing.Commands));

        // ── Platform Services ─────────────────────────────────────────────
        var clipboard = new ClipboardService();
        var paste = new PasteService(clipboard);
        var recorder = new AudioRecorder(cfg.Audio.DeviceIndex, cfg.Audio.MaxSeconds);

        // ── Overlay ───────────────────────────────────────────────────────
        var overlay = new DictationOverlay();

        // ── Agent ─────────────────────────────────────────────────────────
        var agent = new Agent(
            configManager,
            recorder,
            transcriptionProvider,
            pipeline,
            clipboard,
            paste,
            repository,
            overlay,
            loggerFactory.CreateLogger<Agent>());

        // ── WPF Application ───────────────────────────────────────────────
        var wpfApp = new App();
        wpfApp.SetCancellationSource(cts);

        var mainVm = new MainViewModel(agent, repository, configManager, dictionaryService, dictionary, modelManager);
        var mainWindow = new MainWindow(mainVm);

        // When cts is cancelled (e.g. from tray Quit), shut down WPF
        cts.Token.Register(() =>
        {
            wpfApp.Dispatcher.Invoke(() => wpfApp.Shutdown());
        });

        // ── Tray Icon (STA thread) ────────────────────────────────────────
        Action showWindow = () => wpfApp.Dispatcher.Invoke(() =>
        {
            if (!mainWindow.IsVisible)
                mainWindow.Show();
            mainWindow.Activate();
            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;
        });

        var trayManager = new TrayIconManager(cts, showWindow, loggerFactory.CreateLogger<TrayIconManager>());

        var trayThread = new Thread(() =>
        {
            try { trayManager.Run(overlay); }
            catch (Exception ex) { logger.LogError(ex, "Tray icon error"); }
        });
        trayThread.SetApartmentState(ApartmentState.STA);
        trayThread.IsBackground = true;
        trayThread.Name = "TrayIconThread";
        trayThread.Start();

        // ── Agent task (background thread) ───────────────────────────────
        var agentTask = Task.Run(() => agent.RunAsync(cts.Token));

        logger.LogInformation("All services started. Use tray menu to quit.");

        // ── WPF message loop (blocks until Shutdown() called) ─────────────
        wpfApp.Run(mainWindow);

        // ── Cleanup ───────────────────────────────────────────────────────
        cts.Cancel();

        try { agentTask.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }

        mainVm.Dispose();
        agent.Dispose();
        overlay.Dispose();
        trayManager.Dispose();
        db.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));

        logger.LogInformation("TokenTalk stopped.");
    }
}
