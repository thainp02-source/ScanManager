using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Diagnostics;
using System.Drawing;
using WIA;
using System.Runtime.InteropServices;

namespace ScanManager
{
    public partial class PageManagerForm : Form
    {
        private List<string> pages;
        private CancellationTokenSource? thumbnailCancellation;
        private CancellationTokenSource? previewCancellation;

        // Chỉ giải mã một ảnh tại một thời điểm.
        private readonly SemaphoreSlim imageDecodeLock = new(1, 1);
        private bool pageOperationBusy = false;

        public PageManagerForm()
        {
            InitializeComponent();
        }

        public PageManagerForm(List<string> scannedPages)
        {
            InitializeComponent();

            pages = scannedPages;

            LoadPageList();
        }


        private async void LoadPageList(int selectedIndex = -1)
        {
            // Hủy lượt tạo thumbnail trước nếu còn chạy.
            thumbnailCancellation?.Cancel();

            var cts = new CancellationTokenSource();
            thumbnailCancellation = cts;

            lvPages.BeginUpdate();

            try
            {
                lvPages.Items.Clear();
                imageList1.Images.Clear();

                imageList1.ImageSize = new Size(120, 120);
                imageList1.ColorDepth = ColorDepth.Depth32Bit;

                // Hiển thị tên trang ngay, chưa cần đọc ảnh.
                for (int i = 0; i < pages.Count; i++)
                {
                    lvPages.Items.Add(
                        new ListViewItem($"Trang {i + 1}")
                        {
                            ImageIndex = -1
                        });
                }

                if (selectedIndex >= 0 &&
                    selectedIndex < lvPages.Items.Count)
                {
                    lvPages.Items[selectedIndex].Selected = true;
                    lvPages.Items[selectedIndex].EnsureVisible();
                }
            }
            finally
            {
                lvPages.EndUpdate();
            }

            try
            {
                for (int i = 0; i < pages.Count; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    string path = pages[i];

                    if (!File.Exists(path))
                        continue;

                    Bitmap? thumbnail = null;

                    try
                    {
                        // Ngăn nhiều ảnh lớn được giải mã đồng thời.
                        await imageDecodeLock.WaitAsync(cts.Token);

                        try
                        {
                            thumbnail = await Task.Run(
                                () => CreateScaledImage(path, 120, 120),
                                cts.Token);
                        }
                        finally
                        {
                            imageDecodeLock.Release();
                        }

                        if (cts.IsCancellationRequested ||
                            IsDisposed ||
                            !IsHandleCreated)
                        {
                            return;
                        }

                        // ImageList lưu bản sao của thumbnail.
                        imageList1.Images.Add(thumbnail);

                        lvPages.Items[i].ImageIndex =
                            imageList1.Images.Count - 1;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "Lỗi thumbnail: " + ex.Message);
                    }
                    finally
                    {
                        // Không giữ bản Bitmap tạm sau khi thêm.
                        thumbnail?.Dispose();
                    }
                }
            }
            finally
            {
                if (ReferenceEquals(thumbnailCancellation, cts))
                    thumbnailCancellation = null;

                cts.Dispose();
            }
        }



        private async void lvPages_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            previewCancellation?.Cancel();

            if (lvPages.SelectedItems.Count == 0)
            {
                var oldImage = picPreview.Image;
                picPreview.Image = null;
                oldImage?.Dispose();
                return;
            }

            int index = lvPages.SelectedItems[0].Index;

            if (index < 0 || index >= pages.Count)
                return;

            string imagePath = pages[index];

            if (!File.Exists(imagePath))
                return;

            var cts = new CancellationTokenSource();
            previewCancellation = cts;

            Bitmap? newPreview = null;

            try
            {
                await imageDecodeLock.WaitAsync(cts.Token);

                try
                {
                    // Chỉ tạo ảnh xem trước tối đa 1000 x 1400.
                    newPreview = await Task.Run(
                        () => CreateScaledImage(
                            imagePath, 1000, 1400),
                        cts.Token);
                }
                finally
                {
                    imageDecodeLock.Release();
                }

                if (cts.IsCancellationRequested ||
                    IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                picPreview.SizeMode = PictureBoxSizeMode.Zoom;

                var oldImage = picPreview.Image;
                picPreview.Image = newPreview;
                newPreview = null;

                // Giải phóng ảnh xem trước cũ.
                oldImage?.Dispose();
            }
            catch (OperationCanceledException)
            {
                // Người dùng đã chọn trang khác.
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "Lỗi xem trước: " + ex.Message);
            }
            finally
            {
                newPreview?.Dispose();

                if (ReferenceEquals(previewCancellation, cts))
                    previewCancellation = null;

                cts.Dispose();
            }
        }


        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (pageOperationBusy)
                return;
            if (lvPages.SelectedItems.Count == 0)
            {
                MessageBox.Show("Chọn ít nhất một trang cần xóa.");
                return;
            }

            DialogResult result = MessageBox.Show(
                $"Bạn có chắc muốn xóa {lvPages.SelectedItems.Count} trang?",
                "Xác nhận",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            // Lấy danh sách index được chọn
            List<int> indexes = new List<int>();

            foreach (ListViewItem item in lvPages.SelectedItems)
            {
                indexes.Add(item.Index);
            }

            // Xóa từ cuối lên đầu
            indexes.Sort();
            indexes.Reverse();

            foreach (int index in indexes)
            {
                try
                {
                    if (File.Exists(pages[index]))
                    {
                        File.Delete(pages[index]);
                    }
                }
                catch
                {
                }

                pages.RemoveAt(index);
            }

            picPreview.Image?.Dispose();
            picPreview.Image = null;

            LoadPageList();

            MessageBox.Show($"Đã xóa {indexes.Count} trang.");
        }

        private void btnMoveUp_Click(object sender, EventArgs e)
        {
            if (pageOperationBusy)
                return;
            if (lvPages.SelectedItems.Count == 0)
                return;

            int index = lvPages.SelectedItems[0].Index;

            if (index == 0)
                return;

            // đổi vị trí trong List<string>
            (pages[index], pages[index - 1]) =
                (pages[index - 1], pages[index]);

            LoadPageList(index - 1);
        }

        private void btnMoveDown_Click(object sender, EventArgs e)
        {
            if (pageOperationBusy)
                return;
            if (lvPages.SelectedItems.Count == 0)
                return;

            int index = lvPages.SelectedItems[0].Index;

            if (index == pages.Count - 1)
                return;

            (pages[index], pages[index + 1]) =
                (pages[index + 1], pages[index]);

            LoadPageList(index + 1);
        }


        private async void btnRotate_Click(object sender, EventArgs e)
        {
            if (pageOperationBusy)
                return;

            if (lvPages.SelectedItems.Count == 0)
            {
                MessageBox.Show("Chọn một trang cần xoay.");
                return;
            }

            int index = lvPages.SelectedItems[0].Index;

            if (index < 0 || index >= pages.Count)
                return;

            string imagePath = pages[index];

            if (!File.Exists(imagePath))
                return;

            pageOperationBusy = true;
            btnRotate.Enabled = false;
            lvPages.Enabled = false;

            // Ngăn tác vụ xem trước tiếp tục đọc file đang xoay.
            thumbnailCancellation?.Cancel();
            previewCancellation?.Cancel();

            bool lockAcquired = false;

            try
            {
                await imageDecodeLock.WaitAsync();
                lockAcquired = true;

                await Task.Run(() =>
                {
                    string extension = Path
                        .GetExtension(imagePath)
                        .ToLowerInvariant();

                    var format = extension switch
                    {
                        ".jpg" or ".jpeg" =>
                            System.Drawing.Imaging.ImageFormat.Jpeg,

                        ".bmp" =>
                            System.Drawing.Imaging.ImageFormat.Bmp,

                        ".tif" or ".tiff" =>
                            System.Drawing.Imaging.ImageFormat.Tiff,

                        _ => System.Drawing.Imaging.ImageFormat.Png
                    };

                    string tempPath =
                        imagePath + "." +
                        Guid.NewGuid().ToString("N") +
                        ".tmp";

                    try
                    {
                        using (var bitmap = new Bitmap(imagePath))
                        {
                            bitmap.RotateFlip(
                                RotateFlipType.Rotate90FlipNone);

                            bitmap.Save(tempPath, format);
                        }

                        // Chỉ ghi đè sau khi đã đóng Bitmap.
                        File.Move(tempPath, imagePath, true);
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                        {
                            try
                            {
                                File.Delete(tempPath);
                            }
                            catch
                            {
                            }
                        }
                    }
                });

                // Tải lại thumbnail sau khi xoay.
                LoadPageList(index);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không xoay được trang:\n" + ex.Message,
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (lockAcquired)
                    imageDecodeLock.Release();

                pageOperationBusy = false;
                btnRotate.Enabled = true;
                lvPages.Enabled = true;
            }
        }


        private void btnInsertPage_Click(object sender, EventArgs e)
        {
            if (pageOperationBusy)
                return;

            if (lvPages.SelectedItems.Count == 0)
            {
                MessageBox.Show("Chọn vị trí cần chèn.");
                return;
            }

            int index = lvPages.SelectedItems[0].Index;

            if (index < 0 || index >= pages.Count)
                return;

            WIA.CommonDialog? dialog = null;
            WIA.ImageFile? image = null;

            string? imagePath = null;
            bool inserted = false;

            pageOperationBusy = true;
            btnInsertPage.Enabled = false;

            try
            {
                // Khởi tạo hộp thoại WIA.
                dialog = new WIA.CommonDialog();

                // Quét một trang mới.
                image = dialog.ShowAcquireImage(
                    WiaDeviceType.ScannerDeviceType,
                    WiaImageIntent.ColorIntent,
                    WiaImageBias.MaximizeQuality,
                    "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}",
                    false,
                    true,
                    false);

                // Người dùng hủy quét.
                if (image == null)
                    return;

                string tempFolder = Path.Combine(
                    Path.GetTempPath(),
                    "ScanManager");

                Directory.CreateDirectory(tempFolder);

                imagePath = Path.Combine(
                    tempFolder,
                    Guid.NewGuid().ToString("N") + ".png");

                // Lưu ảnh xuống ổ đĩa.
                image.SaveFile(imagePath);

                // Chèn sau trang được chọn.
                pages.Insert(index + 1, imagePath);
                inserted = true;

                // Cập nhật danh sách và chọn trang vừa chèn.
                LoadPageList(index + 1);

                System.Diagnostics.Debug.WriteLine(
                    $"Đã chèn trang mới. Tổng: {pages.Count} trang.");
            }
            catch (Exception ex)
            {
                // Nếu chưa chèn thành công thì xóa ảnh ghi dở.
                if (!inserted && imagePath != null)
                {
                    try
                    {
                        if (File.Exists(imagePath))
                            File.Delete(imagePath);
                    }
                    catch (Exception cleanupEx)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "Lỗi xóa ảnh tạm: " + cleanupEx.Message);
                    }
                }

                MessageBox.Show(
                    "Không thể chèn trang:\n" + ex.Message,
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                // Giải phóng ảnh COM ngay sau khi lưu.
                ReleaseWiaObject(image);

                // Giải phóng hộp thoại WIA.
                ReleaseWiaObject(dialog);

                pageOperationBusy = false;
                btnInsertPage.Enabled = true;
            }
        }


        private async void btnMergePdf_Click(object sender, EventArgs e)
        {
            if (pageOperationBusy)
                return;

            using OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "PDF files|*.pdf",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            string[] pdfFiles = dlg.FileNames;

            string tempFolder = Path.Combine(
                Path.GetTempPath(),
                "ScanManager");

            Directory.CreateDirectory(tempFolder);

            List<string> importedPages = new();
            Exception? importError = null;

            pageOperationBusy = true;
            btnMergePdf.Enabled = false;
            lvPages.Enabled = false;

            try
            {
                // Xử lý PDF trên luồng nền.
                await Task.Run(() =>
                {
                    foreach (string pdfFile in pdfFiles)
                    {
                        using var pdf =
                            PdfiumViewer.PdfDocument.Load(pdfFile);

                        for (int i = 0; i < pdf.PageCount; i++)
                        {
                            // Chỉ render một trang mỗi lần.
                            using Bitmap bitmap =
                                (Bitmap)pdf.Render(
                                    i, 2480, 3508, true);

                            string imagePath = Path.Combine(
                                tempFolder,
                                Guid.NewGuid().ToString("N") + ".png");

                            try
                            {
                                bitmap.Save(
                                    imagePath,
                                    System.Drawing.Imaging.ImageFormat.Png);

                                importedPages.Add(imagePath);
                            }
                            catch
                            {
                                if (File.Exists(imagePath))
                                    File.Delete(imagePath);

                                throw;
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                importError = ex;
            }
            finally
            {
                // Giữ những trang đã nhập thành công nếu bị lỗi giữa chừng.
                if (importedPages.Count > 0)
                {
                    pages.AddRange(importedPages);
                    LoadPageList();
                }

                pageOperationBusy = false;
                btnMergePdf.Enabled = true;
                lvPages.Enabled = true;
            }

            if (importError != null)
            {
                MessageBox.Show(
                    $"Đã nhập được {importedPages.Count} trang.\n" +
                    $"Lỗi:\n{importError.Message}",
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(
                    $"Đã nhập {importedPages.Count} trang từ PDF.",
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();

        }

        private void picPreview_Click(object sender, EventArgs e)
        {

        }

        private static Bitmap CreateScaledImage(
            string imagePath,
            int maxWidth,
            int maxHeight)
        {
            // Chỉ mở ảnh nguồn trong lúc tạo ảnh thu nhỏ.
            using var source = Image.FromFile(imagePath);

            var result = new Bitmap(
                maxWidth,
                maxHeight,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            try
            {
                using (var graphics = Graphics.FromImage(result))
                {
                    graphics.Clear(Color.White);

                    graphics.InterpolationMode =
                        System.Drawing.Drawing2D
                            .InterpolationMode.HighQualityBilinear;

                    double ratio = Math.Min(
                        (double)maxWidth / source.Width,
                        (double)maxHeight / source.Height);

                    int width = Math.Max(
                        1, (int)(source.Width * ratio));

                    int height = Math.Max(
                        1, (int)(source.Height * ratio));

                    int x = (maxWidth - width) / 2;
                    int y = (maxHeight - height) / 2;

                    graphics.DrawImage(
                        source,
                        new Rectangle(x, y, width, height));
                }

                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
        protected override void OnFormClosed(
    FormClosedEventArgs e)
        {
            thumbnailCancellation?.Cancel();
            previewCancellation?.Cancel();

            var oldImage = picPreview.Image;
            picPreview.Image = null;
            oldImage?.Dispose();

            imageList1.Images.Clear();

            base.OnFormClosed(e);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (pageOperationBusy)
            {
                e.Cancel = true;

                MessageBox.Show(
                    "Đang xử lý trang tài liệu. " +
                    "Vui lòng hoàn thành thao tác trước khi đóng.",
                    "Scan Manager");

                return;
            }

            base.OnFormClosing(e);
        }

        private static void ReleaseWiaObject(object? obj)
        {
            if (obj == null)
                return;

            try
            {
                if (Marshal.IsComObject(obj))
                {
                    Marshal.ReleaseComObject(obj);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "Lỗi giải phóng WIA COM: " + ex.Message);
            }
        }


    }
}