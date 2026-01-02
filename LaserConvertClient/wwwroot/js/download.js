// File download helper for Blazor WASM
window.downloadFile = function (fileName, base64Content, mimeType) {
    const link = document.createElement('a');
    link.href = `data:${mimeType};base64,${base64Content}`;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

// Initialize drop zone for drag-and-drop file handling
window.initializeDropZone = function (dropZoneElement, inputFileElement) {
    // Prevent default drag behaviors on the document
    ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
        dropZoneElement.addEventListener(eventName, preventDefaults, false);
    });

    function preventDefaults(e) {
        e.preventDefault();
        e.stopPropagation();
    }

    // Handle dropped files by forwarding them to the InputFile element
    dropZoneElement.addEventListener('drop', function (e) {
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            // Create a new DataTransfer to set files on the input
            const dataTransfer = new DataTransfer();
            for (let i = 0; i < files.length; i++) {
                dataTransfer.items.add(files[i]);
            }
            inputFileElement.files = dataTransfer.files;
            
            // Trigger the change event so Blazor picks up the files
            const event = new Event('change', { bubbles: true });
            inputFileElement.dispatchEvent(event);
        }
    });
};
