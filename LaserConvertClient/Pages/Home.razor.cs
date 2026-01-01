using LaserConvertProcess;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace LaserConvertClient.Pages;

public partial class Home
{
    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private double thickness = 3.0;
    private double tolerance = 0.5;
    private bool debugMode = false;
    private string outputText = "";
    private bool isDragging = false;
    private bool isProcessing = false;

    private void HandleDragEnter(DragEventArgs e)
    {
        isDragging = true;
    }

    private void HandleDragLeave(DragEventArgs e)
    {
        isDragging = false;
    }

    private void HandleDragOver(DragEventArgs e)
    {
        // Required to allow drop
    }

    private void HandleDrop(DragEventArgs e)
    {
        isDragging = false;
        // File handling is done by InputFile component
    }

    private async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        // Clear output for new batch
        outputText = "";
        isProcessing = true;
        StateHasChanged();

        var files = e.GetMultipleFiles(100); // Allow up to 100 files
        var validFiles = files.Where(f =>
            f.Name.EndsWith(".stp", StringComparison.OrdinalIgnoreCase) ||
            f.Name.EndsWith(".step", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (validFiles.Count == 0)
        {
            outputText = "No valid STEP files found. Please drop .stp or .step files.";
            isProcessing = false;
            StateHasChanged();
            return;
        }

        foreach (var file in validFiles)
        {
            await ProcessFile(file);
        }

        isProcessing = false;
        StateHasChanged();
    }

    private async Task ProcessFile(IBrowserFile file)
    {
        try
        {
            AppendOutput($"=== Processing: {file.Name} ===");

            // Read file contents
            using var stream = file.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024); // 50MB max
            using var reader = new StreamReader(stream);
            var fileContents = await reader.ReadToEndAsync();

            // Create processing options with message callback
            var options = new ProcessingOptions
            {
                Thickness = thickness,
                ThicknessTolerance = tolerance,
                DebugMode = debugMode,
                OnMessage = (message, isDebugOnly) =>
                {
                    AppendOutput(message);
                    InvokeAsync(StateHasChanged);
                }
            };

            // Process the file
            var result = StepProcess.Process(fileContents, options);

            if (result.ReturnCode == 1 && !string.IsNullOrEmpty(result.SVGContents))
            {
                // Generate output filename
                var outputFileName = Path.GetFileNameWithoutExtension(file.Name) + ".svg";

                // Trigger download
                await DownloadFile(outputFileName, result.SVGContents);
                AppendOutput($"? Saved: {outputFileName}");
            }
            else if (result.ReturnCode == 2)
            {
                AppendOutput($"? Error processing {file.Name}");
            }
            else
            {
                AppendOutput($"? No valid output generated for {file.Name}");
            }

            AppendOutput(""); // Blank line between files
        }
        catch (Exception ex)
        {
            AppendOutput($"? Error: {ex.Message}");
        }
    }

    private void AppendOutput(string message)
    {
        if (!string.IsNullOrEmpty(outputText))
        {
            outputText += "\n";
        }
        outputText += message;
    }

    private async Task DownloadFile(string fileName, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var base64 = Convert.ToBase64String(bytes);
        await JS.InvokeVoidAsync("downloadFile", fileName, base64, "image/svg+xml");
    }
}
