using System.Text;
using System.Text.RegularExpressions;

namespace FixMyEncoding;

public static partial class Program
{
    private static readonly string[] allowedFileExtensions =
    [
        ".cs",
        ".csproj",
        ".sln",
        ".props",
        ".targets",
        ".config",
        ".c",
        ".h",
        ".cpp",
        ".hpp",
        ".cc",
        ".cxx",
        ".java",
        ".kt",
        ".kts",
        ".scala",
        ".py",
        ".pyi",
        ".rb",
        ".php",
        ".go",
        ".rs",
        ".swift",
        ".js",
        ".jsx",
        ".ts",
        ".tsx",
        ".mjs",
        ".cjs",
        ".vue",
        ".svelte",
        ".html",
        ".htm",
        ".css",
        ".scss",
        ".sass",
        ".less",
        ".json",
        ".jsonc",
        ".xml",
        ".xaml",
        ".yaml",
        ".yml",
        ".toml",
        ".ini",
        ".sql",
        ".sh",
        ".bash",
        ".zsh",
        ".ps1",
        ".bat",
        ".cmd",
        ".md",
        ".markdown",
        ".txt",
        ".csv",
        ".tsv",
        ".log",
        ".gitignore",
        ".gitattributes",
        ".editorconfig",
        ".dockerignore",
    ];

    private static readonly string[] allowedExtensionlessFileNames =
    [
        "dockerfile",
        "makefile",
        "license",
        "readme",
    ];

    [GeneratedRegex(@"\.[a-zA-Z0-9]{1,4}")]
    private static partial Regex _extensionRegex();

    private static bool IsMedia(byte[] b)
    {
        bool Starts(params byte[] sig) =>
            b.Length >= sig.Length && b.AsSpan(0, sig.Length).SequenceEqual(sig);
        bool At(int offset, string s) =>
            b.Length >= offset + s.Length && Encoding.ASCII.GetString(b, offset, s.Length) == s;

        return Starts(0xFF, 0xD8, 0xFF)
            || Starts(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
            || At(0, "GIF8")
            || Starts("BM"u8.ToArray())
            || (At(0, "RIFF") && (At(8, "WEBP") || At(8, "AVI ")))
            || At(4, "ftyp")
            || Starts(0x1A, 0x45, 0xDF, 0xA3);
    }

    private static string? TryReadTextFile(byte[] bytes, out bool wasUTF8, int? windowsCodePage)
    {
        wasUTF8 = false;
        if (bytes.Length == 0)
            return null;

        if (IsMedia(bytes))
            return null;

        bool Starts(params byte[] signature) =>
            bytes.Length >= signature.Length
            && bytes.AsSpan(0, signature.Length).SequenceEqual(signature);

        string fileText;
        int bomLength = 0;
        try
        {
            if (Starts(0xFF, 0xFE, 0x00, 0x00))
            {
                fileText = new UTF32Encoding(false, true, true).GetString(bytes);
                bomLength = 4;
            }
            else if (Starts(0x00, 0x00, 0xFE, 0xFF))
            {
                fileText = new UTF32Encoding(true, true, true).GetString(bytes);
                bomLength = 4;
            }
            else if (Starts(0xFF, 0xFE))
            {
                fileText = new UnicodeEncoding(false, true, true).GetString(bytes);
                bomLength = 2;
            }
            else if (Starts(0xFE, 0xFF))
            {
                fileText = new UnicodeEncoding(true, true, true).GetString(bytes);
                bomLength = 2;
            }
            else
            {
                fileText = new UTF8Encoding(false, true).GetString(bytes);
                wasUTF8 = true;
                bomLength = Starts(0xEF, 0xBB, 0xBF) ? 3 : 0;
            }

            return bomLength > 0 && fileText.Length > 0 && fileText[0] == '\uFEFF'
                ? fileText[1..]
                : fileText;
        }
        catch (DecoderFallbackException) { }

        int sample = Math.Min(bytes.Length, 8000);
        int control = 0;
        for (int i = 0; i < sample; i++)
        {
            var b = bytes[i];
            if (b == 0)
                return null;
            if (b < 0x20 && b != 9 && b != 10 && b != 13 && b != 12 && b != 27)
                control++;
        }
        if (control * 4 > sample || windowsCodePage == null)
            return null;

        try
        {
            fileText = Encoding
                .GetEncoding(
                    windowsCodePage.Value,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback
                )
                .GetString(bytes);
            foreach (var c in fileText)
            {
                if (
                    c == '\uFFFD'
                    || (char.IsControl(c) && c != '\t' && c != '\r' && c != '\n' && c != '\f')
                )
                    return null;
            }
            return fileText;
        }
        catch (DecoderFallbackException) { }
        return null;
    }

    private static void Process(
        string path,
        ref int scannedDirs,
        ref int scannedFiles,
        ref int editedFiles,
        ref int errors,
        IEnumerable<string> extensions,
        int? windowsCodePage,
        bool includeExtensionlessFiles,
        bool isDryRun,
        int maxDepth = 0
    )
    {
        void Log(string message, bool isError = false)
        {
            if (!isDryRun)
                Console.WriteLine(message);
            else if (isError)
                Console.WriteLine("[DRY RUN ERROR] " + message);
        }
        if (!extensions.Any())
        {
            Log("Provided 0 extensions to filter - all files will be skipped (aborting)");
            return;
        }
        if (path == null || !Path.Exists(path))
        {
            Log("Path not found (skipping): " + path);
            return;
        }
        if (Directory.Exists(path))
        {
            Log("Scanning folder: " + path);
            scannedDirs++;
            if (maxDepth != 0)
                foreach (var childDir in Directory.GetDirectories(path))
                    Process(
                        childDir,
                        ref scannedDirs,
                        ref scannedFiles,
                        ref editedFiles,
                        ref errors,
                        extensions,
                        windowsCodePage,
                        includeExtensionlessFiles,
                        isDryRun,
                        maxDepth > 0 ? maxDepth - 1 : maxDepth
                    );
            foreach (var childFile in Directory.GetFiles(path))
                Process(
                    childFile,
                    ref scannedDirs,
                    ref scannedFiles,
                    ref editedFiles,
                    ref errors,
                    extensions,
                    windowsCodePage,
                    includeExtensionlessFiles,
                    isDryRun,
                    maxDepth
                );
        }
        else if (File.Exists(path))
        {
            Log("Scanning file: " + path);
            scannedFiles++;
            try
            {
                string fileName = Path.GetFileName(path);
                string extension = Path.GetExtension(fileName);
                bool allowedByName =
                    includeExtensionlessFiles
                    && allowedExtensionlessFileNames.Contains(
                        fileName,
                        StringComparer.OrdinalIgnoreCase
                    );
                bool allowedByExtension = extensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase
                );

                if (!allowedByName && !allowedByExtension)
                {
                    Log("Skipping file (extension does not match): " + path);
                    return;
                }

                string? text = TryReadTextFile(
                    File.ReadAllBytes(path),
                    out bool wasUTF8,
                    windowsCodePage
                );
                if (text == null)
                {
                    Log("Skipping file (not recognized as supported text file): " + path);
                    return;
                }

                bool hadCRLF = text.Contains("\r\n");
                if (hadCRLF)
                    text = text.Replace("\r\n", "\n");

                bool hadCR = text.Contains('\r');
                if (hadCR)
                    text = text.Replace("\r", "\n");

                if (!wasUTF8 || hadCRLF || hadCR)
                {
                    Log(
                        $"Editing file (wasUTF8={wasUTF8} hadCRLF={hadCRLF} hadCR={hadCR}): {path}"
                    );
                    if (!isDryRun)
                        File.WriteAllText(path, text, Encoding.UTF8);
                    Log($"Writing done: " + path);
                    editedFiles++;
                }
            }
            catch (Exception e)
            {
                errors++;
                Log("An error occurred while processing file (skipping): " + path, isError: true);
                Log(e.GetType() + ": " + e.Message, isError: true);
                if (e.StackTrace != null)
                    Log(e.StackTrace, isError: true);
            }
        }
        else
        {
            Log("Failed to resolve path (skipping): " + path);
        }
    }

    public static void Main()
    {
        Console.WriteLine(
            "Welcome to FixMyEncoding - tool for setting up UTF-8 with LF for your code files!\n"
        );

        Console.WriteLine("MAKE A BACKUP FIRST:");
        Console.WriteLine(" - This program will recursively read and WRITE your files");
        Console.WriteLine(
            " - This program MAY CORRUPT your files if it encounters misleading file extensions or uncommon encodings"
        );
        Console.WriteLine(
            " - This program MAY CORRUPT your files if it is interrupted while writing a file"
        );
        Console.WriteLine(
            " - This program DOES NOT BACK UP your files and CANNOT REVERT its actions\n"
        );

        Console.Write("Do you want to proceed? [y/N]: ");

        if (!Console.ReadLine()!.Trim().Equals("y", StringComparison.CurrentCultureIgnoreCase))
        {
            Console.WriteLine("Aborting.");
            return;
        }

        Console.WriteLine("\nPreparing encoding provider...");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.WriteLine("Done.\n");

        Console.Write("Enter a folder path (or leave empty to use the current directory)\n> ");
        string folderPath = Console.ReadLine()!.Trim();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            Console.WriteLine("Attempting to use the current directory");
            folderPath = Directory.GetCurrentDirectory();
        }
        else
        {
            folderPath = Path.GetFullPath(folderPath);
        }

        Console.WriteLine("Using directory: " + folderPath);

        Console.Write(
            "Enter file extensions to include (or leave empty to include all supported files)\n> "
        );
        List<string> fileExtensions = [];
        MatchCollection extensionMatches = _extensionRegex().Matches(Console.ReadLine()!);
        if (extensionMatches.Count > 0)
            for (int i = 0; i < extensionMatches.Count; i++)
                if (extensionMatches[i].Success)
                    fileExtensions.Add(extensionMatches[i].Value.ToLower());

        if (fileExtensions.Count == 0)
        {
            Console.WriteLine("Using default extension list...");
            fileExtensions.AddRange(allowedFileExtensions);
        }
        else
        {
            List<string> dangerExtensions =
            [
                .. fileExtensions.Where(ext => !allowedFileExtensions.Contains(ext)),
            ];
            if (dangerExtensions.Count > 0)
            {
                Console.WriteLine(
                    "CAREFUL HERE: You specified some extensions that are not present in this program's known text file extension list"
                );
                Console.WriteLine("Suspicious extensions: " + string.Join(' ', dangerExtensions));
                Console.WriteLine(
                    " - Make sure that you didn't make any typos in your extensions list"
                );
                Console.WriteLine(
                    " - Make sure that files with these extensions are REALLY text files"
                );
                Console.WriteLine(
                    " - Make sure that those files use one of the following encodings: UTF-8, UTF-16, UTF-32, Windows-1251 (CP1251), Windows-1252 (CP1252)\n"
                );

                Console.Write("Do you want to proceed? [y/N]: ");
                if (
                    !Console
                        .ReadLine()!
                        .Trim()
                        .Equals("y", StringComparison.CurrentCultureIgnoreCase)
                )
                {
                    Console.WriteLine("Aborting.");
                    return;
                }
            }
        }

        Console.Write(
            "Include extensionless files such as Dockerfile, Makefile, LICENSE and README? [Y/n]: "
        );
        bool includeExtensionlessFiles = !Console
            .ReadLine()!
            .Trim()
            .Equals("n", StringComparison.CurrentCultureIgnoreCase);

        Console.WriteLine("How should non-Unicode files be handled?");
        Console.WriteLine(
            "These files are usually in a legacy Windows encoding, which cannot be detected reliably:"
        );
        Console.WriteLine(
            "  1 - Windows-1251: choose for files from Russian, Ukrainian, Bulgarian or other Cyrillic Windows locales"
        );
        Console.WriteLine(
            "  2 - Windows-1252: choose for files from English, German, French, Spanish or other Western European Windows locales"
        );
        Console.WriteLine(
            "  3 - Skip these files: choose if you do not expect legacy Windows-encoded files or are unsure"
        );
        Console.Write("Your choice [1/2/3, default: 3]: ");
        int? windowsCodePage = Console.ReadLine()!.Trim() switch
        {
            "1" => 1251,
            "2" => 1252,
            _ => null,
        };

        Console.Write(
            "Enter max depth (use a negative value or leave empty for unlimited depth)\n> "
        );
        if (!int.TryParse(Console.ReadLine()!, out int maxDepth))
        {
            Console.WriteLine("Failed to parse integer, using default value (-1)");
            maxDepth = -1;
        }

        string[] messages =
        [
            "Change encoding to UTF-8",
            "And change line endings to LF",
            "Non-Unicode files: "
                + (windowsCodePage == null ? "skip" : $"decode as Windows-{windowsCodePage}"),
            "Extensionless files (Dockerfile, Makefile, LICENSE, README): "
                + (includeExtensionlessFiles ? "include" : "skip"),
            "To "
                + (
                    fileExtensions.Count == 0
                        ? "ALL supported text files"
                        : "supported text files with extensions: "
                            + string.Join(' ', fileExtensions)
                ),
            "Inside folder: " + folderPath,
            "Max depth: " + maxDepth + (maxDepth < 0 ? " (unlimited)" : ""),
        ];

        Console.WriteLine("Please wait. Performing dry run...");

        int dry_scannedDirs = 0;
        int dry_scannedFiles = 0;
        int dry_editedFiles = 0;
        int dry_errors = 0;
        Process(
            folderPath,
            ref dry_scannedDirs,
            ref dry_scannedFiles,
            ref dry_editedFiles,
            ref dry_errors,
            isDryRun: true,
            extensions: fileExtensions,
            windowsCodePage: windowsCodePage,
            includeExtensionlessFiles: includeExtensionlessFiles,
            maxDepth: maxDepth
        );

        if (dry_errors > 0)
            Console.WriteLine(
                $"\n {dry_errors} ERRORS occurred during dry run. Make sure you have checked the error logs above."
            );

        Console.Write("Do you want to proceed? [y/N]: ");
        if (!Console.ReadLine()!.Trim().Equals("y", StringComparison.CurrentCultureIgnoreCase))
        {
            Console.WriteLine("Aborting.");
            return;
        }

        int width;
        try
        {
            width = Console.WindowWidth;
        }
        catch (IOException)
        {
            width = 40;
        }

        if (width < 40)
            width = 40;

        string separator = new('-', width);
        string title = "CHECK IF THIS IS CORRECT";
        int titlePadding = (separator.Length - title.Length) / 2;
        Console.WriteLine(separator);
        Console.WriteLine(new string(' ', titlePadding) + title);
        Console.WriteLine(separator);
        foreach (var message in messages)
            Console.WriteLine(message);
        Console.WriteLine(separator);

        Console.WriteLine("Dry-run complete (no files are modified yet).");
        Console.WriteLine("If you want to proceed, this program will:");
        Console.WriteLine($" - SCAN around {dry_scannedDirs} directories");
        Console.WriteLine($" - READ around {dry_scannedFiles} files");
        Console.WriteLine($" - WRITE around {dry_editedFiles} files WITHOUT ANY BACKUPS");

        Console.Write("\n Do you really want to proceed? [y/N]: ");
        if (!Console.ReadLine()!.Trim().Equals("y", StringComparison.CurrentCultureIgnoreCase))
        {
            Console.WriteLine("Aborting.");
            return;
        }

        Console.WriteLine("Starting...");
        int scannedDirs = 0;
        int scannedFiles = 0;
        int editedFiles = 0;
        int errors = 0;
        Process(
            folderPath,
            ref scannedDirs,
            ref scannedFiles,
            ref editedFiles,
            ref errors,
            isDryRun: false,
            extensions: fileExtensions,
            windowsCodePage: windowsCodePage,
            includeExtensionlessFiles: includeExtensionlessFiles,
            maxDepth: maxDepth
        );
        Console.WriteLine(
            $"Done. Folders scanned: {scannedDirs}. Files scanned: {scannedFiles}. Files edited: {editedFiles}. Errors: {errors}"
        );
    }
}
