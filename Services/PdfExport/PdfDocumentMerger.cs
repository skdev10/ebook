using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EBookDashboard.Services.PdfExport;

/// <summary>Joins Chromium chunk PDFs and inserts a blank page so each later chunk still opens on a recto.</summary>
public static class PdfDocumentMerger
{
    public static byte[] MergeChapterChunks(IReadOnlyList<byte[]> chunks)
    {
        if (chunks == null || chunks.Count == 0)
            throw new InvalidOperationException("No PDF chunks to merge.");
        if (chunks.Count == 1)
            return chunks[0];

        using var output = new PdfDocument();
        var runningPages = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            using var ms = new MemoryStream(chunks[i]);
            using var src = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            if (src.PageCount == 0)
                continue;

            // Page 1 of the book is a recto. An odd running count means the next page would be a verso.
            if (i > 0 && runningPages % 2 == 1)
            {
                var blank = output.AddPage();
                blank.Width = src.Pages[0].Width;
                blank.Height = src.Pages[0].Height;
                runningPages++;
            }

            for (var p = 0; p < src.PageCount; p++)
                output.AddPage(src.Pages[p]);
            runningPages += src.PageCount;
        }

        using var outMs = new MemoryStream();
        output.Save(outMs, false);
        return outMs.ToArray();
    }
}
