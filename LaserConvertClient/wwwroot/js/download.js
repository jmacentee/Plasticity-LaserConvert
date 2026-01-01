// File download helper for Blazor WASM
window.downloadFile = function (fileName, base64Content, mimeType) {
    const link = document.createElement('a');
    link.href = `data:${mimeType};base64,${base64Content}`;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
