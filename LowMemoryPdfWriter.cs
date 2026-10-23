
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ScanManager
{
    public static class LowMemoryPdfWriter
    {
        // Đọc thông số JPEG mà không giải mã cả ảnh.
        private static (int Width, int Height, int Components)
            ReadJpegInfo(string path)
        {
            using var stream = File.OpenRead(path);

            int ReadRequired()
            {
                int value = stream.ReadByte();
                if (value < 0)
                    throw new InvalidDataException("JPEG bị thiếu dữ liệu.");
                return value;
            }

            int ReadWord()
            {
                return (ReadRequired() << 8) | ReadRequired();
            }

            if (ReadRequired() != 0xFF || ReadRequired() != 0xD8)
                throw new InvalidDataException("Không phải ảnh JPEG: " + path);

            while (stream.Position < stream.Length)
            {
                if (ReadRequired() != 0xFF)
                    throw new InvalidDataException("JPEG marker không hợp lệ.");

                int marker;
                do
                {
                    marker = ReadRequired();
                }
                while (marker == 0xFF);

                if (marker == 0x01 ||
                    (marker >= 0xD0 && marker <= 0xD7))
                    continue;

                if (marker == 0xDA || marker == 0xD9)
                    break;

                int length = ReadWord();

                if (length < 2 ||
                    stream.Position + length - 2 > stream.Length)
                {
                    throw new InvalidDataException(
                        "JPEG segment không hợp lệ.");
                }

                bool isFrame =
                    marker >= 0xC0 &&
                    marker <= 0xCF &&
                    marker != 0xC4 &&
                    marker != 0xC8 &&
                    marker != 0xCC;

                if (isFrame)
                {
                    int precision = ReadRequired();
                    int height = ReadWord();
                    int width = ReadWord();
                    int components = ReadRequired();

                    if (precision != 8 ||
                        width <= 0 || height <= 0 ||
                        (components != 1 && components != 3))
                    {
                        throw new InvalidDataException(
                            "JPEG không có định dạng màu được hỗ trợ.");
                    }

                    return (width, height, components);
                }

                stream.Seek(length - 2, SeekOrigin.Current);
            }

            throw new InvalidDataException(
                "Không tìm thấy thông tin kích thước JPEG.");
        }

        public static void CreatePdf(
            IReadOnlyList<string> imagePaths,
            string pdfPath)
        {
            if (imagePaths.Count == 0)
                throw new InvalidOperationException(
                    "Không có ảnh để tạo PDF.");

            string tempPath =
                pdfPath + "." +
                Guid.NewGuid().ToString("N") + ".partial";

            try
            {
                using (var output = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    65536,
                    FileOptions.SequentialScan))
                {
                    int totalObjects = 2 + imagePaths.Count * 3;
                    long[] offsets = new long[totalObjects + 1];

                    void Write(string text)
                    {
                        byte[] bytes = Encoding.ASCII.GetBytes(text);
                        output.Write(bytes, 0, bytes.Length);
                    }

                    void BeginObject(int id)
                    {
                        offsets[id] = output.Position;
                        Write($"{id} 0 obj\n");
                    }

                    Write("%PDF-1.4\n");

                    // PDF Catalog
                    BeginObject(1);
                    Write("<< /Type /Catalog /Pages 2 0 R >>\n");
                    Write("endobj\n");

                    // Danh sách các trang PDF
                    BeginObject(2);

                    Write(
                        $"<< /Type /Pages /Count {imagePaths.Count} /Kids [");

                    for (int i = 0; i < imagePaths.Count; i++)
                        Write($"{3 + i * 3} 0 R ");

                    Write("] >>\nendobj\n");

                    const double pageWidth = 595.28;
                    const double pageHeight = 841.89;

                    // Chỉ xử lý một ảnh JPEG tại mỗi thời điểm.
                    for (int i = 0; i < imagePaths.Count; i++)
                    {
                        string imagePath = imagePaths[i];

                        var info = ReadJpegInfo(imagePath);

                        int pageId = 3 + i * 3;
                        int contentId = pageId + 1;
                        int imageId = pageId + 2;

                        double ratio = Math.Min(
                            pageWidth / info.Width,
                            pageHeight / info.Height);

                        double drawWidth = info.Width * ratio;
                        double drawHeight = info.Height * ratio;

                        double x = (pageWidth - drawWidth) / 2;
                        double y = (pageHeight - drawHeight) / 2;

                        // Trang PDF
                        BeginObject(pageId);

                        Write(
                            "<< /Type /Page /Parent 2 0 R " +
                            "/MediaBox [0 0 595.28 841.89] " +
                            "/Resources << /XObject << " +
                            $"/Im0 {imageId} 0 R >> >> " +
                            $"/Contents {contentId} 0 R >>\n");

                        Write("endobj\n");

                        // Lệnh hiển thị ảnh trong trang
                        string commands = string.Format(
                            CultureInfo.InvariantCulture,
                            "q\n{0:0.###} 0 0 {1:0.###} " +
                            "{2:0.###} {3:0.###} cm\n" +
                            "/Im0 Do\nQ\n",
                            drawWidth, drawHeight, x, y);

                        byte[] content =
                            Encoding.ASCII.GetBytes(commands);

                        BeginObject(contentId);

                        Write($"<< /Length {content.Length} >>\nstream\n");
                        output.Write(content, 0, content.Length);
                        Write("\nendstream\nendobj\n");

                        // Nhúng nguyên JPEG, không giải mã ra Bitmap.
                        using (var jpeg = new FileStream(
                            imagePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            65536,
                            FileOptions.SequentialScan))
                        {
                            string colorSpace =
                                info.Components == 1
                                    ? "/DeviceGray"
                                    : "/DeviceRGB";

                            BeginObject(imageId);

                            Write(
                                "<< /Type /XObject /Subtype /Image " +
                                $"/Width {info.Width} /Height {info.Height} " +
                                $"/ColorSpace {colorSpace} " +
                                "/BitsPerComponent 8 /Filter /DCTDecode " +
                                $"/Length {jpeg.Length} >>\nstream\n");

                            // Copy theo từng khối 64 KB.
                            jpeg.CopyTo(output, 65536);

                            Write("\nendstream\nendobj\n");
                        }
                    }

                    // Bảng tham chiếu PDF
                    long xrefPosition = output.Position;

                    if (xrefPosition > 9999999999L)
                        throw new InvalidOperationException(
                            "PDF vượt giới hạn kích thước hỗ trợ.");

                    Write($"xref\n0 {totalObjects + 1}\n");
                    Write("0000000000 65535 f \n");

                    for (int id = 1; id <= totalObjects; id++)
                    {
                        if (offsets[id] > 9999999999L)
                            throw new InvalidOperationException(
                                "PDF vượt giới hạn tham chiếu.");

                        Write(
                            offsets[id].ToString(
                                "D10",
                                CultureInfo.InvariantCulture) +
                            " 00000 n \n");
                    }

                    Write(
                        $"trailer\n<< /Size {totalObjects + 1} " +
                        "/Root 1 0 R >>\n" +
                        $"startxref\n{xrefPosition}\n%%EOF\n");

                    output.Flush(true);
                }

                // Đã tạo xong mới chuyển thành PDF chính thức.
                File.Move(tempPath, pdfPath);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }

                // Không xóa ảnh quét nếu tạo PDF thất bại.
                throw;
            }
        }
    }
}
