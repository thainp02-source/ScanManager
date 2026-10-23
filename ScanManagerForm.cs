using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Diagnostics;
using System.Windows.Forms;
using WIA;
using Microsoft.Data.Sqlite;
using System.Threading;
using NTwain;
using NTwain.Data;
using System.Runtime.InteropServices;

namespace ScanManager
{
    public partial class ScanManagerForm : Form
    {
        private int currentNumber = 1;
        private string? lastPdfPath;
        private List<string> scannedPages = new();
        private readonly List<DeviceInfo> scannerDevices = new();
        private string selectedFilePath = "";
        private volatile bool stopScanRequested = false;
        private bool isScanning = false;
        private bool isSavingPdf = false;
        private Thread? scanThread;
        private NTwain.TwainSession? twainSession;
        private NTwain.DataSource? twainSource;
        private NTwain.WindowsFormsMessageLoopHook? twainMessageHook;
        private readonly List<string> twainScannedPages = new();
        private bool autoBatchMode = false;
        private bool autoBatchStopping = false;
        private bool twainBatchActive = false;

        private int autoBatchNumber = 1;

        private System.Windows.Forms.Timer? autoBatchTimer;

        private const int AUTO_BATCH_RETRY_MS = 3000;
        private int autoBatchRetryCount = 0;
        private const int AUTO_BATCH_MAX_RETRIES = 20;

        private bool twainTransferFailed = false;
        private string twainTransferError = "";
        private readonly string dbPath =
        Path.Combine(
        Application.StartupPath,
        "documents.db");

        private sealed class ScanOptions
        {
            public string DeviceId { get; init; } = "";
            public int Dpi { get; init; } = 300;
            public int ColorMode { get; init; } = 1;
            public bool Duplex { get; init; }
        }

        public ScanManagerForm()
        {
            InitializeComponent();
            dgvDocuments.CellDoubleClick += dgvDocuments_CellDoubleClick;
            dgvDocuments.Columns.Add("FileName", "Tên File");
            dgvDocuments.Columns.Add("DocumentType", "Loại");
            dgvDocuments.Columns.Add("CreatedDate", "Ngày Tạo");
            dgvDocuments.Columns.Add("FilePath", "Đường dẫn");
            DataGridViewButtonColumn actionColumn = new();

            actionColumn.Name = "Action";
            actionColumn.HeaderText = "Thao tác";
            actionColumn.Text = "⚙";
            actionColumn.UseColumnTextForButtonValue = true;

            dgvDocuments.Columns.Add(actionColumn);

            CreateDatabase();
            LoadDocuments();

            cmbFileType.Items.AddRange(new[]
            {
                "PDF",
                "JPEG",
                "PNG",
                "TIFF",
                "BMP"
            });

            cmbFileType.SelectedIndex = 0;
            // Cấu hình danh sách máy scan
            cmbScanner.DropDownStyle = ComboBoxStyle.DropDownList;

            // Sự kiện làm mới danh sách máy scan
            btnRefreshScanner.Click -= btnRefreshScanner_Click;
            btnRefreshScanner.Click += btnRefreshScanner_Click;
            btnStopScan.Click += btnStopScan_Click;

            // Tải danh sách thiết bị
            LoadScanners();
            InitializeScanSettings();
            //Shown += (sender, e) => CheckTwainScanner();



        }

        private void LoadScanners()
        {
            try
            {
                string? previousDeviceId = null;

                if (cmbScanner.SelectedIndex >= 0 &&
                    cmbScanner.SelectedIndex < scannerDevices.Count)
                {
                    previousDeviceId =
                        scannerDevices[cmbScanner.SelectedIndex].DeviceID;
                }

                cmbScanner.Items.Clear();
                scannerDevices.Clear();

                DeviceManager manager = new DeviceManager();

                foreach (DeviceInfo deviceInfo in manager.DeviceInfos)
                {
                    if (deviceInfo.Type != WiaDeviceType.ScannerDeviceType)
                        continue;

                    string deviceName = deviceInfo.DeviceID;

                    try
                    {
                        foreach (Property property in deviceInfo.Properties)
                        {
                            if (property.Name == "Name")
                            {
                                deviceName = property.get_Value()?.ToString()
                                    ?? deviceInfo.DeviceID;
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Sử dụng DeviceID nếu không đọc được tên
                    }

                    scannerDevices.Add(deviceInfo);
                    cmbScanner.Items.Add(deviceName);
                }

                if (scannerDevices.Count > 0)
                {
                    int previousIndex = scannerDevices.FindIndex(
                        x => x.DeviceID == previousDeviceId);

                    cmbScanner.SelectedIndex =
                        previousIndex >= 0 ? previousIndex : 0;
                }
                else
                {
                    MessageBox.Show(
                        "Không tìm thấy máy scan.\n" +
                        "Vui lòng kiểm tra kết nối USB và driver.",
                        "Scan Manager",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể tải danh sách máy scan:\n" + ex.Message,
                    "Lỗi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        private void btnRefreshScanner_Click(object? sender, EventArgs e)
        {
            LoadScanners();
        }


        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using FolderBrowserDialog dlg = new FolderBrowserDialog();

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                txtFolder.Text = dlg.SelectedPath;
            }
        }

        private void ScanAndSaveSinglePage(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(txtFileName.Text))
                {
                    MessageBox.Show("Nhập tên file");
                    return;
                }

                if (string.IsNullOrWhiteSpace(txtFolder.Text))
                {
                    MessageBox.Show("Chọn thư mục lưu");
                    return;
                }

                WIA.CommonDialog dialog = new WIA.CommonDialog();

                string formatId = GetWiaFormat(cmbFileType.Text);

                ImageFile image = dialog.ShowAcquireImage(
                    WiaDeviceType.ScannerDeviceType,
                    WiaImageIntent.ColorIntent,
                    WiaImageBias.MaximizeQuality,
                    formatId,
                    false,
                    true,
                    false);

                if (image == null)
                    return;

                string saveFolder = Path.Combine(
                  txtFolder.Text,
                  txtDocumentType.Text);
                //DateTime.Now.ToString("yyyy-MM-dd"));

                Directory.CreateDirectory(saveFolder);

                string fileType = cmbFileType.Text;


                if (fileType == "PDF")
                {
                    string imagePath = Path.Combine(
                        saveFolder,
                        txtFileName.Text + ".png");

                    image.SaveFile(imagePath);

                    string pdfPath =
                        Path.ChangeExtension(
                            imagePath,
                            ".pdf");

                    CreatePdfFromImage(
                        imagePath,
                        pdfPath);
                    if (File.Exists(imagePath))
                    {
                        File.Delete(imagePath);
                    }

                    lastPdfPath = pdfPath;

                    SaveDocument(
                        Path.GetFileName(pdfPath),
                        txtDocumentType.Text,
                        pdfPath);

                    LoadDocuments();

                    MessageBox.Show(
                        $"Đã lưu PDF:\n{pdfPath}");
                }
                else if (fileType == "PNG")
                {
                    string pngPath = Path.Combine(
                        saveFolder,
                        txtFileName.Text + ".png");

                    image.SaveFile(pngPath);

                    SaveDocument(
                        Path.GetFileName(pngPath),
                        txtDocumentType.Text,
                        pngPath);

                    LoadDocuments();

                    MessageBox.Show(
                        $"Đã lưu PNG:\n{pngPath}");
                }
                else if (fileType == "JPEG")
                {
                    string jpgPath = Path.Combine(
                        saveFolder,
                        txtFileName.Text + ".jpg");

                    image.SaveFile(jpgPath);

                    SaveDocument(
                        Path.GetFileName(jpgPath),
                        txtDocumentType.Text,
                        jpgPath);

                    LoadDocuments();

                    MessageBox.Show(
                        $"Đã lưu JPG:\n{jpgPath}");
                }
                else if (fileType == "BMP")
                {
                    string bmpPath = Path.Combine(
                        saveFolder,
                        txtFileName.Text + ".bmp");

                    image.SaveFile(bmpPath);

                    SaveDocument(
                        Path.GetFileName(bmpPath),
                        txtDocumentType.Text,
                        bmpPath);

                    LoadDocuments();

                    MessageBox.Show(
                        $"Đã lưu BMP:\n{bmpPath}");
                }
                else if (fileType == "TIFF")
                {
                    string tifPath = Path.Combine(
                        saveFolder,
                        txtFileName.Text + ".tiff");

                    image.SaveFile(tifPath);

                    SaveDocument(
                        Path.GetFileName(tifPath),
                        txtDocumentType.Text,
                        tifPath);

                    LoadDocuments();

                    MessageBox.Show(
                        $"Đã lưu TIFF:\n{tifPath}");
                }

                IncreaseFileName();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void CreatePdfFromImage(
            string imagePath,
            string pdfPath)
        {
            CreatePdfFromScanImages(
                new[] { imagePath },
                pdfPath);
        }


        private void CreateDatabase()
        {
            using SqliteConnection conn =
                new($"Data Source={dbPath}");

            conn.Open();

            string sql = @"
             CREATE TABLE IF NOT EXISTS Documents
             (
                 Id INTEGER PRIMARY KEY AUTOINCREMENT,
                 FileName TEXT,
                 DocumentType TEXT,
                 FilePath TEXT,
                 CreatedDate TEXT
             )";

            using SqliteCommand cmd =
                new(sql, conn);

            cmd.ExecuteNonQuery();
        }
        private void SaveDocument(
         string fileName,
         string documentType,
         string filePath)
        {
            using SqliteConnection conn =
                new($"Data Source={dbPath}");

            conn.Open();

            string sql = @"
             INSERT INTO Documents
             (
                 FileName,
                 DocumentType,
                 FilePath,
                 CreatedDate
             )
             VALUES
             (
                 @FileName,
                 @DocumentType,
                 @FilePath,
                 @CreatedDate
             )";

            using SqliteCommand cmd =
                new(sql, conn);

            cmd.Parameters.AddWithValue(
                "@FileName",
                fileName);

            cmd.Parameters.AddWithValue(
                "@DocumentType",
                documentType);

            cmd.Parameters.AddWithValue(
                "@FilePath",
                filePath);

            cmd.Parameters.AddWithValue(
                "@CreatedDate",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

            cmd.ExecuteNonQuery();
        }

        private void LoadDocuments()
        {
            dgvDocuments.SuspendLayout();

            try
            {
                dgvDocuments.Rows.Clear();

                using SqliteConnection conn =
                    new($"Data Source={dbPath}");

                conn.Open();

                // Chỉ tải 200 tài liệu mới nhất.
                // Không xóa dữ liệu cũ trong SQLite.
                const string sql = @"
            SELECT FileName, DocumentType,
                   CreatedDate, FilePath
            FROM Documents
            ORDER BY Id DESC
            LIMIT 200";

                using SqliteCommand cmd = new(sql, conn);
                using SqliteDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    string filePath =
                        reader["FilePath"]?.ToString() ?? "";

                    if (!File.Exists(filePath))
                        continue;

                    dgvDocuments.Rows.Add(
                        reader["FileName"],
                        reader["DocumentType"],
                        reader["CreatedDate"],
                        filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "LoadDocuments: " + ex.Message);
            }
            finally
            {
                dgvDocuments.ResumeLayout();
            }
        }


        private void btnScanPage_Click(object? sender, EventArgs e)
        {
            try
            {
                if (chkDuplex.Checked || chkContinuousScan.Checked)
                {
                    MessageBox.Show(
                        "Bước này chỉ hỗ trợ quét một mặt, một trang.\n" +
                        "Vui lòng bỏ chọn Duplex và Scan liên tục.",
                        "Scan Manager",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                string imagePath = ScanSinglePage();

                scannedPages.Add(imagePath);

                MessageBox.Show(
                    $"Đã quét thành công.\n" +
                    $"Tổng số trang: {scannedPages.Count}",
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Lỗi khi quét:\n" + ex.Message,
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }


        private async void btnFinishPdf_Click(
            object? sender,
            EventArgs e)
        {
            if (isScanning || autoBatchMode || isSavingPdf)
                return;

            if (scannedPages.Count == 0)
            {
                MessageBox.Show("Chưa có trang nào để lưu.");
                return;
            }

            string rootFolder = txtFolder.Text.Trim();
            string documentType = txtDocumentType.Text.Trim();
            string fileName = txtFileName.Text.Trim();

            if (string.IsNullOrWhiteSpace(rootFolder) ||
                string.IsNullOrWhiteSpace(fileName))
            {
                MessageBox.Show(
                    "Vui lòng chọn thư mục và nhập tên file.");
                return;
            }

            if (fileName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("Tên file không hợp lệ.");
                return;
            }

            // Chụp lại danh sách trang trước khi lưu.
            List<string> pages = scannedPages.ToList();

            isSavingPdf = true;
            SetScanningState(false);

            try
            {
                string saveFolder = Path.Combine(
                    rootFolder,
                    documentType);

                Directory.CreateDirectory(saveFolder);

                string pdfPath = Path.Combine(
                    saveFolder,
                    fileName + ".pdf");

                // Tránh ghi đè tài liệu cũ.
                if (File.Exists(pdfPath))
                {
                    pdfPath = Path.Combine(
                        saveFolder,
                        fileName + "_" +
                        DateTime.Now.ToString(
                            "yyyyMMdd_HHmmss_fff") +
                        ".pdf");
                }

                // Xử lý PDF ở luồng nền.
                await Task.Run(() =>
                    CreatePdfFromScanImages(pages, pdfPath));

                // Sau await, cập nhật giao diện và SQLite.
                SaveDocument(
                    Path.GetFileName(pdfPath),
                    documentType,
                    pdfPath);

                lastPdfPath = pdfPath;

                LoadDocuments();

                // Chỉ xóa ảnh gốc khi PDF và SQLite
                // đều đã xử lý thành công.
                foreach (string path in pages)
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "Không xóa được ảnh quét: " +
                            ex.Message);
                    }
                }

                scannedPages.Clear();

                MessageBox.Show(
                    $"Đã lưu PDF {pages.Count} trang:\n{pdfPath}",
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Lỗi lưu PDF:\n" + ex.Message +
                    "\n\nCác ảnh quét gốc vẫn được giữ lại.",
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                isSavingPdf = false;
                SetScanningState(false);
            }
        }

        private string GetWiaFormat(string fileType)
        {
            return fileType switch
            {
                "PNG" =>
                    "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}",

                "JPEG" =>
                    "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}",

                "BMP" =>
                    "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}",

                "TIFF" =>
                    "{B96B3CB1-0728-11D3-9D7B-0000F81EF32E}",

                _ =>
                    "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}"
            };
        }
        private void IncreaseFileName()
        {
        }




        private void dgvDocuments_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            if (dgvDocuments.Columns[e.ColumnIndex].Name == "Action")
            {
                selectedFilePath =
                    dgvDocuments.Rows[e.RowIndex]
                    .Cells["FilePath"]
                    .Value?.ToString() ?? "";

                Rectangle rect =
                    dgvDocuments.GetCellDisplayRectangle(
                        e.ColumnIndex,
                        e.RowIndex,
                        true);

                contextMenuStrip1.Show(
                    dgvDocuments,
                    rect.Left,
                    rect.Bottom);
            }
        }
        private void dgvDocuments_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            string filePath =
                dgvDocuments.Rows[e.RowIndex]
                .Cells["FilePath"]
                .Value?.ToString() ?? "";

            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("File không tồn tại.");
            }
        }


        private void mởTàiLiệuToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (File.Exists(selectedFilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = selectedFilePath,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("File không tồn tại.");
            }
        }

        private void mởThưMụcChứaFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (File.Exists(selectedFilePath))
            {
                Process.Start(
                    "explorer.exe",
                    "/select,\"" + selectedFilePath + "\"");
            }
            else
            {
                MessageBox.Show("File không tồn tại.");
            }
        }

        private void inLạiToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (File.Exists(selectedFilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = selectedFilePath,
                    Verb = "print",
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("File không tồn tại.");
            }
        }

        private void xóaToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!File.Exists(selectedFilePath))
            {
                MessageBox.Show("File không tồn tại.");
                return;
            }

            DialogResult result = MessageBox.Show(
                "Bạn có chắc muốn xóa tài liệu này?",
                "Xác nhận",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                // Xóa file vật lý
                File.Delete(selectedFilePath);

                // Xóa trong SQLite
                //using SqliteConnection conn =
                //    new($"Data Source={dbPath}");


                //conn.Open();

                //string sql =
                //    "DELETE FROM Documents WHERE FilePath=@FilePath";

                //using SqliteCommand cmd =
                //    new(sql, conn);

                //cmd.Parameters.AddWithValue(
                //    "@FilePath",
                //    selectedFilePath);

                //cmd.ExecuteNonQuery();

                // Load lại danh sách
                LoadDocuments();

                MessageBox.Show("Đã xóa thành công.");
            }
        }

        private void btnManagePages_Click(object sender, EventArgs e)
        {
            if (isScanning || isSavingPdf || autoBatchMode)
                return;

            using (PageManagerForm frm =
                new PageManagerForm(scannedPages))
            {
                frm.ShowDialog(this);
            }
        }

        private void InitializeScanSettings()
        {
            // Độ phân giải
            cmbDpi.Items.Clear();
            cmbDpi.Items.AddRange(new object[]
            {
        "200 DPI",
        "300 DPI",
        "600 DPI"
            });
            cmbDpi.SelectedIndex = 1;

            // Chế độ màu
            cmbColorMode.Items.Clear();
            cmbColorMode.Items.AddRange(new object[]
            {
        "Màu",
        "Xám",
        "Đen trắng"
            });
            cmbColorMode.SelectedIndex = 0;

            // Nguồn giấy
            cmbScanSource.Items.Clear();
            cmbScanSource.Items.AddRange(new object[]
            {
        "ADF (Nạp giấy tự động)",
        "Flatbed (Mặt kính)"
            });
            cmbScanSource.SelectedIndex = 0;

            // Giá trị mặc định
            chkDuplex.Checked = false;
            chkContinuousScan.Checked = false;

            // Checkbox: Mỗi trang 1 file PDF
            chkOnePageOneFile.Text = "Mỗi tờ  1 file PDF";
            chkOnePageOneFile.AutoSize = true;
            chkOnePageOneFile.ForeColor = Color.Black;
            chkOnePageOneFile.Visible = true;
            chkOnePageOneFile.Checked = false;
            chkOnePageOneFile.UseVisualStyleBackColor = true;

            // Không cho nhập ngoài danh sách
            cmbDpi.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbColorMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbScanSource.DropDownStyle = ComboBoxStyle.DropDownList;

            // Timer Auto Batch
            autoBatchTimer = new System.Windows.Forms.Timer
            {
                Interval = AUTO_BATCH_RETRY_MS
            };

            autoBatchTimer.Tick += AutoBatchTimer_Tick;
        }

        private void AutoBatchTimer_Tick(object? sender, EventArgs e)
        {
            autoBatchTimer?.Stop();

            if (!autoBatchMode || autoBatchStopping)
                return;

            if (twainBatchActive || isScanning || isSavingPdf)
                return;

            StartNextAutoBatch();
        }
        private int GetSelectedDpi()
        {
            string dpiText = cmbDpi.Text.Replace("DPI", "").Trim();

            return int.TryParse(dpiText, out int dpi)
                ? dpi
                : 300;
        }
        private static bool TrySetWiaProperty(
    WIA.Properties properties,
    int propertyId,
    object value)
        {
            foreach (WIA.Property property in properties)
            {
                if (property.PropertyID != propertyId)
                    continue;

                try
                {
                    property.set_Value(value);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }


        private void ConfigureScanner( WIA.Device device, WIA.Item scanItem, ScanOptions options)
        {
            // 1. Cấu hình độ phân giải
            bool dpiX = TrySetWiaProperty(
                scanItem.Properties, 6147, options.Dpi);

            bool dpiY = TrySetWiaProperty(
                scanItem.Properties, 6148, options.Dpi);

            if (!dpiX || !dpiY)
            {
                throw new InvalidOperationException(
                    $"Driver không chấp nhận độ phân giải {options.Dpi} DPI.");
            }

            // 2. Cấu hình màu
            bool colorConfigured = TrySetWiaProperty(
                scanItem.Properties, 6146, options.ColorMode);

            if (!colorConfigured)
            {
                throw new InvalidOperationException(
                    "Driver không chấp nhận chế độ màu đã chọn.");
            }

            // 3. Cấu hình nguồn giấy
            const int DOCUMENT_HANDLING_SELECT = 3088;
            const int FEEDER = 0x1;
            const int FLATBED = 0x2;
            const int DUPLEX = 0x4;

            // Đọc cấu hình hiện tại
            int? currentMode = GetWiaIntProperty(
                device.Properties,
                DOCUMENT_HANDLING_SELECT);

            // Driver không có thuộc tính này:
            // Có thể là máy quét mặt kính
            if (!currentMode.HasValue)
            {
                if (options.Duplex)
                {
                    throw new InvalidOperationException(
                        "Máy scan không hỗ trợ quét hai mặt.");
                }

                return;
            }

            // Chỉ yêu cầu Duplex khi người dùng bật
            if (options.Duplex)
            {
                bool duplexConfigured = TrySetWiaProperty(
                    device.Properties,
                    DOCUMENT_HANDLING_SELECT,
                    FEEDER | DUPLEX);

                if (!duplexConfigured)
                {
                    throw new InvalidOperationException(
                        "Driver không hỗ trợ ADF Duplex.");
                }

                int? actualMode = GetWiaIntProperty(
                    device.Properties,
                    DOCUMENT_HANDLING_SELECT);

                if (!actualMode.HasValue ||
                    (actualMode.Value & DUPLEX) == 0)
                {
                    throw new InvalidOperationException(
                        "Driver không xác nhận chế độ Duplex.");
                }

                return;
            }

            // 4. Thử bật ADF
            bool adfConfigured = TrySetWiaProperty(
                device.Properties,
                DOCUMENT_HANDLING_SELECT,
                FEEDER);

            if (adfConfigured)
            {
                int? actualMode = GetWiaIntProperty(
                    device.Properties,
                    DOCUMENT_HANDLING_SELECT);

                if (actualMode.HasValue &&
                    (actualMode.Value & FEEDER) != 0)
                {
                    return;
                }
            }

            // 5. Nếu ADF không được hỗ trợ thì thử Flatbed
            bool flatbedConfigured = TrySetWiaProperty(
                device.Properties,
                DOCUMENT_HANDLING_SELECT,
                FLATBED);

            if (!flatbedConfigured)
            {
                // Một số driver Flatbed không cho ghi thuộc tính 3088.
                // Không chặn việc quét một mặt.
                Debug.WriteLine(
                    "WIA: Driver không cho đổi nguồn giấy, giữ cấu hình mặc định.");
            }
        }

        private static int? GetWiaIntProperty(
            WIA.Properties properties,
            int propertyId)
        {
            foreach (WIA.Property property in properties)
            {
                if (property.PropertyID == propertyId)
                {
                    return Convert.ToInt32(property.get_Value());
                }
            }

            return null;
        }



        private WIA.Device ConnectSelectedScanner()
        {
            int index = cmbScanner.SelectedIndex;

            if (index < 0 || index >= scannerDevices.Count)
            {
                throw new InvalidOperationException(
                    "Vui lòng chọn máy scan.");
            }

            DeviceInfo deviceInfo = scannerDevices[index];

            return deviceInfo.Connect();
        }


        private string ScanSinglePage()
        {
            WIA.Device? device = null;
            WIA.Item? scanItem = null;
            WIA.ImageFile? image = null;

            const string PNG_FORMAT =
                "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";

            try
            {
                device = ConnectSelectedScanner();

                if (device.Items.Count == 0)
                    throw new InvalidOperationException(
                        "Không tìm thấy bộ phận quét.");

                scanItem = device.Items[1];

                ConfigureScanner(
                    device,
                    scanItem,
                    GetScanOptions());

                image = (WIA.ImageFile)
                    scanItem.Transfer(PNG_FORMAT);

                string tempFolder = Path.Combine(
                    Path.GetTempPath(),
                    "ScanManager");

                Directory.CreateDirectory(tempFolder);

                string imagePath = Path.Combine(
                    tempFolder,
                    Guid.NewGuid().ToString("N") + ".png");

                try
                {
                    image.SaveFile(imagePath);

                    return imagePath;
                }
                catch
                {
                    if (File.Exists(imagePath))
                        File.Delete(imagePath);

                    throw;
                }
            }
            finally
            {
                ReleaseWiaObject(image);
                ReleaseWiaObject(scanItem);
                ReleaseWiaObject(device);
            }
        }

        private static bool IsAdfEmpty(Exception ex)
        {
            const int WIA_ERROR_PAPER_EMPTY = unchecked((int)0x80210003);

            return ex.HResult == WIA_ERROR_PAPER_EMPTY;
        }



        private void ScanAdfPages(
            ScanOptions options,
            List<string> pages)
        {
            WIA.Device? device = null;
            WIA.Item? scanItem = null;

            const string PNG_FORMAT =
                "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";

            try
            {
                // Kết nối máy quét trên luồng STA.
                device = ConnectScanner(options.DeviceId);

                if (device.Items.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Không tìm thấy bộ phận quét.");
                }

                scanItem = device.Items[1];

                ConfigureScanner(device, scanItem, options);

                string tempFolder = Path.Combine(
                    Path.GetTempPath(),
                    "ScanManager");

                Directory.CreateDirectory(tempFolder);

                while (!stopScanRequested)
                {
                    WIA.ImageFile? image = null;
                    string? imagePath = null;

                    try
                    {
                        try
                        {
                            image = (WIA.ImageFile)
                                scanItem.Transfer(PNG_FORMAT);
                        }
                        catch (Exception ex) when (IsAdfEmpty(ex))
                        {
                            // Khay ADF hết giấy: kết thúc bình thường.
                            break;
                        }

                        imagePath = Path.Combine(
                            tempFolder,
                            Guid.NewGuid().ToString("N") + ".png");

                        // Lưu ảnh xuống ổ đĩa.
                        image.SaveFile(imagePath);

                        // Danh sách chỉ lưu đường dẫn.
                        pages.Add(imagePath);

                        Debug.WriteLine(
                            $"WIA: Đã quét {pages.Count} trang.");
                    }
                    catch
                    {
                        // Xóa ảnh đang ghi dở khi xảy ra lỗi.
                        if (imagePath != null)
                        {
                            try
                            {
                                if (File.Exists(imagePath))
                                    File.Delete(imagePath);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(
                                    "Lỗi xóa ảnh tạm: " + ex.Message);
                            }
                        }

                        throw;
                    }
                    finally
                    {
                        // Giải phóng COM sau TỪNG trang.
                        ReleaseWiaObject(image);
                    }
                }
            }
            finally
            {
                // Giải phóng thiết bị sau mỗi lượt quét.
                ReleaseWiaObject(scanItem);
                ReleaseWiaObject(device);
            }
        }

        private static void CreatePdfFromScanImages(
            IReadOnlyList<string> pages,
            string pdfPath)
        {
            List<string> jpegPaths = new();
            List<string> temporaryJpegs = new();

            try
            {
                foreach (string imagePath in pages)
                {
                    if (!File.Exists(imagePath))
                    {
                        throw new FileNotFoundException(
                            "Không tìm thấy ảnh đã quét.",
                            imagePath);
                    }

                    string extension = Path
                        .GetExtension(imagePath)
                        .ToLowerInvariant();

                    // Ảnh TWAIN đã là JPEG:
                    // không cần giải mã hoặc tạo bản sao.
                    if (extension == ".jpg" ||
                        extension == ".jpeg")
                    {
                        jpegPaths.Add(imagePath);
                        continue;
                    }

                    // Ảnh PNG từ WIA:
                    // chỉ chuyển MỘT ảnh tại một thời điểm.
                    string jpegPath = Path.Combine(
                        Path.GetTempPath(),
                        "ScanManager",
                        Guid.NewGuid().ToString("N") + ".jpg");

                    // Đưa vào danh sách dọn dẹp trước khi ghi.
                    temporaryJpegs.Add(jpegPath);

                    using (var image =
                        System.Drawing.Image.FromFile(imagePath))
                    {
                        // Hàm SaveScanJpeg đã thêm ở bước 1.
                        SaveScanJpeg(image, jpegPath);
                    }

                    jpegPaths.Add(jpegPath);
                }

                // Chỉ nhúng JPEG bằng luồng đọc/ghi.
                LowMemoryPdfWriter.CreatePdf(
                    jpegPaths,
                    pdfPath);
            }
            finally
            {
                // Xóa JPEG trung gian dù thành công hay lỗi.
                // KHÔNG xóa ảnh quét gốc ở đây.
                foreach (string path in temporaryJpegs)
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "Không xóa được JPEG tạm: " + ex.Message);
                    }
                }
            }
        }

        private void StopAutoBatchScan()
        {
            autoBatchStopping = true;
            autoBatchMode = false;

            autoBatchTimer?.Stop();

            // Không đóng TWAIN tại đây.
            // Nếu TWAIN còn đang truyền ảnh,
            // sự kiện SourceDisabled sẽ hoàn tất việc thu hồi trang.
            twainBatchActive = false;

            if (!isScanning)
            {
                SetScanningState(false);
            }

            Debug.WriteLine("AUTO BATCH: Đã yêu cầu dừng.");
        }


        private void btnStopScan_Click(object? sender, EventArgs e)
        {
            stopScanRequested = true;

            if (!autoBatchMode)
            {
                btnStopScan.Enabled = false;
                return;
            }

            autoBatchStopping = true;
            autoBatchTimer?.Stop();

            btnStopScan.Enabled = false;

            // Nếu đang chờ tập mới, dừng ngay.
            if (!isScanning && !isSavingPdf)
            {
                StopAutoBatchScan();
                return;
            }

            // Đang quét TWAIN:
            // Không đóng DataSource giữa lúc truyền ảnh.
            // Chờ lượt hiện tại kết thúc để lưu đủ trang.
            Debug.WriteLine(
                "AUTO BATCH: Yêu cầu dừng sau lượt quét hiện tại.");
        }

        private void SetScanningState(bool scanning)
        {
            isScanning = scanning;

            // Auto Batch vẫn được xem là đang hoạt động
            // kể cả khi đã hết giấy và đang chờ tập mới.
            bool busy = scanning || autoBatchMode || isSavingPdf;

            btnScan.Enabled = !busy;
            btnScanPage.Enabled = !busy;

            // Cho phép dừng khi đang quét hoặc đang chờ giấy.
            btnStopScan.Enabled = (scanning || autoBatchMode) && !autoBatchStopping;

            btnRefreshScanner.Enabled = !busy;

            cmbScanner.Enabled = !busy;
            cmbDpi.Enabled = !busy;
            cmbColorMode.Enabled = !busy;
            cmbScanSource.Enabled = !busy;

            chkDuplex.Enabled = !busy;
            chkContinuousScan.Enabled = !busy;
            chkOnePageOneFile.Enabled = !busy;
            btnFinishPdf.Enabled = !busy;
            btnManagePages.Enabled = !busy;
        }


        private void StartAdfScan()
        {
            if (isScanning)
                return;

            //if (chkDuplex.Checked)
            //{
            //    MessageBox.Show(
            //        "Hiện tại đang thử nghiệm ADF một mặt.\n" +
            //        "Vui lòng bỏ chọn Quét hai mặt.");
            //    return;
            //}

            ScanOptions options;

            try
            {
                // Đọc cấu hình trên luồng giao diện
                options = GetScanOptions();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }

            stopScanRequested = false;
            SetScanningState(true);

            scanThread = new Thread(() =>
            {
                List<string> pages = new();
                Exception? scanError = null;

                try
                {
                    ScanAdfPages(options, pages);
                }
                catch (Exception ex)
                {
                    scanError = ex;
                }

                try
                {
                    if (IsDisposed || !IsHandleCreated)
                        return;

                    BeginInvoke(new Action(() =>
                    {
                        // Giữ cả các trang đã quét trước khi xảy ra lỗi
                        scannedPages.AddRange(pages);

                        SetScanningState(false);

                        if (scanError != null)
                        {
                            MessageBox.Show(
                                $"Đã nhận {pages.Count} trang.\n\n" +
                                $"Lỗi Scan:\n{scanError.Message}",
                                "Scan Manager",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                        else
                        {
                            MessageBox.Show(
                                $"Đã quét {pages.Count} trang.\n" +
                                $"Tổng số trang: {scannedPages.Count}",
                                "Scan Manager",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    // Form đã đóng
                }
            });

            scanThread.IsBackground = true;
            scanThread.SetApartmentState(ApartmentState.STA);
            scanThread.Start();
        }




        private void btnScan_Click(object sender, EventArgs e)
        {
            if (isScanning || autoBatchMode || isSavingPdf)
                return;

            // Scan liên tục: tự động lưu nhiều tập tài liệu
            if (chkContinuousScan.Checked)
            {
                StartAutoBatchScan();
                return;
            }

            // Một trang một PDF hoặc quét Duplex:
            // sử dụng TWAIN của Canon.
            if (chkOnePageOneFile.Checked || chkDuplex.Checked)
            {
                StartTwainDuplexScan();
                return;
            }

            // Không tích gì:
            // Quét ADF một mặt, nhấn Hoàn tất PDF để lưu.
            StartAdfScan();
        }


        private void StartAutoBatchScan()
        {
            if (autoBatchMode || isScanning || isSavingPdf)
                return;

            if (cmbScanner.SelectedIndex < 0)
            {
                MessageBox.Show("Vui lòng chọn máy scan.");
                return;
            }

            if (!cmbScanner.Text.Contains(
                "DR-C225",
                StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Auto Batch hiện chỉ hỗ trợ Canon DR-C225 II.");
                return;
            }

            if (string.IsNullOrWhiteSpace(txtFolder.Text))
            {
                MessageBox.Show("Vui lòng chọn thư mục lưu.");
                return;
            }

            if (scannedPages.Count > 0)
            {
                MessageBox.Show(
                    "Bạn đang có trang quét chưa hoàn thành.\n" +
                    "Vui lòng lưu các trang đó trước khi bắt đầu Auto Batch.");
                return;
            }

            autoBatchMode = true;
            autoBatchStopping = false;
            twainBatchActive = false;

            autoBatchNumber = 1;
            stopScanRequested = false;
            autoBatchRetryCount = 0;
            twainTransferFailed = false;
            twainTransferError = "";

            autoBatchTimer?.Stop();

            StartNextAutoBatch();
        }
        private void StartNextAutoBatch()
        {
            if (!autoBatchMode || autoBatchStopping)
                return;

            if (twainBatchActive || isScanning || isSavingPdf)
                return;

            autoBatchTimer?.Stop();

            if (autoBatchRetryCount >= AUTO_BATCH_MAX_RETRIES)
            {
                StopAutoBatchScan();

                MessageBox.Show(
                    "Đã hết thời gian chờ tập tài liệu mới.\n" +
                    "Nhấn Scan để bắt đầu phiên mới.",
                    "Auto Batch",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            twainBatchActive = true;
            autoBatchRetryCount++;

            try
            {
                StartTwainDuplexScan();

                if (!isScanning)
                {
                    twainBatchActive = false;

                    if (autoBatchMode && !autoBatchStopping)
                    {
                        autoBatchTimer?.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                twainBatchActive = false;

                Debug.WriteLine(
                    "AUTO BATCH: Lỗi mở TWAIN: " + ex.Message);

                if (autoBatchMode && !autoBatchStopping)
                {
                    autoBatchTimer?.Start();
                }
            }
        }

        private void StartTwainDuplexScan()
        {
            if (isScanning)
                return;
            twainTransferFailed = false;
            twainTransferError = "";

            try
            {
                var appId = NTwain.Data.TWIdentity.CreateFromAssembly(
                    NTwain.Data.DataGroups.Image,
                    typeof(ScanManagerForm).Assembly);

                twainSession = new NTwain.TwainSession(appId);

                // Dùng vòng lặp thông điệp của WinForms
                twainMessageHook =
                    new NTwain.WindowsFormsMessageLoopHook(this.Handle);

                // Nhận ảnh sau khi scan
                twainSession.DataTransferred +=
                    TwainSession_DataTransferred;

                // Nhận thông báo kết thúc
                twainSession.SourceDisabled +=
                    TwainSession_SourceDisabled;

                // Theo dõi lỗi truyền ảnh
                twainSession.TransferError += (s, e) =>
                {
                    twainTransferFailed = true;
                    twainTransferError = "Driver TWAIN báo lỗi truyền ảnh.";

                    Debug.WriteLine("TWAIN: TransferError = " + e);
                };

                // Mở TWAIN bằng MessageLoopHook dành cho WinForms
                var openResult = twainSession.Open(twainMessageHook);

                if (openResult != NTwain.Data.ReturnCode.Success)
                {
                    throw new InvalidOperationException(
                        "Không mở được TWAIN: " + openResult);
                }

                twainSource = twainSession.GetSources()
                    .FirstOrDefault(x =>
                        x.Name.Contains(
                            "DR-C225",
                            StringComparison.OrdinalIgnoreCase));

                if (twainSource == null)
                    throw new InvalidOperationException(
                        "Không tìm thấy Canon DR-C225 TWAIN.");

                twainSource.Open();
                // Lấy chế độ màu từ giao diện
                var pixelType = cmbColorMode.SelectedIndex switch
                {
                    1 => NTwain.Data.PixelType.Gray,  // Xám
                    2 => NTwain.Data.PixelType.BlackWhite, // Đen trắng
                    _ => NTwain.Data.PixelType.RGB    // Màu
                };

                // Cấu hình chế độ màu cho TWAIN
                var colorResult =
                    twainSource.Capabilities.ICapPixelType.SetValue(pixelType);

                Debug.WriteLine($"TWAIN Color: {colorResult}");

                // Bật khay nạp giấy tự động ADF
                var feederResult =
                    twainSource.Capabilities.CapFeederEnabled.SetValue(
                        NTwain.Data.BoolType.True);

                // Bật quét hai mặt
                var duplexResult =
                    twainSource.Capabilities.CapDuplexEnabled.SetValue(
                        chkDuplex.Checked
                            ? NTwain.Data.BoolType.True
                            : NTwain.Data.BoolType.False);

                System.Diagnostics.Debug.WriteLine(
                    $"ADF: {feederResult}, Duplex: {duplexResult}");

                if (duplexResult != NTwain.Data.ReturnCode.Success)
                {
                    throw new InvalidOperationException(
                        chkDuplex.Checked
                            ? "Không bật được chế độ quét hai mặt."
                            : "Không chuyển được máy scan sang chế độ một mặt.");
                }

                twainScannedPages.Clear();

                SetScanningState(true);

                // Kích hoạt driver để bắt đầu quét
                var result = twainSource.Enable(
                    NTwain.SourceEnableMode.NoUI,
                    false,
                    this.Handle);

                System.Diagnostics.Debug.WriteLine(
                    $"TWAIN Enable: {result}");

                if (result != NTwain.Data.ReturnCode.Success)
                {
                    throw new InvalidOperationException(
                        $"Không kích hoạt được TWAIN: {result}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "TWAIN: Lỗi khởi động: " + ex);

                try
                {
                    if (twainSession != null)
                    {
                        twainSession.DataTransferred -=
                            TwainSession_DataTransferred;

                        twainSession.SourceDisabled -=
                            TwainSession_SourceDisabled;
                    }

                    twainSource?.Close();
                    twainSession?.Close();
                }
                catch (Exception cleanupEx)
                {
                    Debug.WriteLine(
                        "TWAIN: Lỗi đóng phiên: " + cleanupEx.Message);
                }
                finally
                {
                    twainSource = null;
                    twainSession = null;
                    twainBatchActive = false;

                    SetScanningState(false);
                }

                if (autoBatchMode && !autoBatchStopping)
                {
                    // Không hiện MessageBox liên tục khi đang chờ giấy.
                    Debug.WriteLine(
                        "AUTO BATCH: Sẽ thử lại sau 3 giây.");

                    autoBatchTimer?.Start();
                }
                else
                {
                    MessageBox.Show(
                        "Lỗi quét TWAIN:\n" + ex.Message,
                        "Scan Manager",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }


        private static void SaveScanJpeg(
            System.Drawing.Image image,
            string filePath)
        {
            var jpegEncoder =
                System.Drawing.Imaging.ImageCodecInfo
                    .GetImageEncoders()
                    .FirstOrDefault(x =>
                        x.FormatID ==
                        System.Drawing.Imaging.ImageFormat.Jpeg.Guid);

            if (jpegEncoder == null)
            {
                throw new InvalidOperationException(
                    "Không tìm thấy bộ mã hóa JPEG.");
            }

            using var parameters =
                new System.Drawing.Imaging.EncoderParameters(1);

            parameters.Param[0] =
                new System.Drawing.Imaging.EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality,
                    90L);

            image.Save(
                filePath,
                jpegEncoder,
                parameters);
        }



        private void TwainSession_DataTransferred(
            object? sender,
            NTwain.DataTransferredEventArgs e)
        {
            string? imagePath = null;

            try
            {
                // Nhận dữ liệu ảnh từ Canon
                using var imageStream = e.GetNativeImageStream();

                if (imageStream == null)
                {
                    throw new InvalidOperationException(
                        "Driver TWAIN trả về ảnh rỗng.");
                }

                // Thư mục chứa ảnh quét tạm
                string tempFolder = Path.Combine(
                    Path.GetTempPath(),
                    "ScanManager");

                Directory.CreateDirectory(tempFolder);

                imagePath = Path.Combine(
                    tempFolder,
                    Guid.NewGuid().ToString("N") + ".jpg");

                // Chỉ tạo một đối tượng ảnh.
                // Lưu xuống ổ đĩa, sau đó giải phóng RAM.
                using (var image = Image.FromStream(imageStream))
                {
                    SaveScanJpeg(image, imagePath);
                }

                // Chỉ giữ đường dẫn, không giữ ảnh trong RAM.
                twainScannedPages.Add(imagePath);

                Debug.WriteLine(
                    $"TWAIN: Đã quét {twainScannedPages.Count} trang.");
            }
            catch (Exception ex)
            {
                twainTransferFailed = true;
                twainTransferError = ex.Message;

                // Xóa ảnh chưa lưu hoàn chỉnh nếu xảy ra lỗi.
                if (imagePath != null)
                {
                    try
                    {
                        if (File.Exists(imagePath))
                            File.Delete(imagePath);
                    }
                    catch (Exception cleanupEx)
                    {
                        Debug.WriteLine(
                            "Lỗi xóa ảnh tạm: " + cleanupEx.Message);
                    }
                }

                Debug.WriteLine(
                    "TWAIN: Lỗi nhận ảnh: " + ex);
            }
        }


        private async void FinishTwainScan()
        {
            if (twainSession == null)
                return;

            List<string> completedPages = twainScannedPages.ToList();
            twainScannedPages.Clear();

            try
            {
                twainSession.DataTransferred -=
                    TwainSession_DataTransferred;

                twainSession.SourceDisabled -=
                    TwainSession_SourceDisabled;

                twainSource?.Close();
                twainSession.Close();

                twainSource = null;
                twainSession = null;
            }
            catch (Exception ex)
            {
                scannedPages.AddRange(completedPages);

                twainSource = null;
                twainSession = null;
                twainBatchActive = false;

                autoBatchMode = false;
                autoBatchStopping = true;
                autoBatchTimer?.Stop();

                SetScanningState(false);

                MessageBox.Show(
                    "Lỗi đóng TWAIN:\n" + ex.Message +
                    "\n\nCác ảnh đã nhận được giữ lại.",
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            twainBatchActive = false;
            SetScanningState(false);

            // Nếu có lỗi nhận ảnh thì không tự tạo PDF,
            // tránh lưu tài liệu thiếu trang.
            if (twainTransferFailed)
            {
                scannedPages.AddRange( completedPages.Where(File.Exists));

                autoBatchMode = false;
                autoBatchStopping = true;
                autoBatchTimer?.Stop();

                SetScanningState(false);

                MessageBox.Show(
                    "Có lỗi trong quá trình nhận ảnh TWAIN:\n" +
                    twainTransferError +
                    "\n\nĐã giữ lại " + completedPages.Count +
                    " trang nhận thành công.\n" +
                    "Vui lòng kiểm tra trước khi lưu PDF.",
                    "Lỗi quét tài liệu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            // Quét TWAIN thông thường.

            // Quét TWAIN không dùng Auto Batch.
            if (!autoBatchMode)
            {
                // Tùy chọn mỗi trang một PDF.
                if (chkOnePageOneFile.Checked &&
                    completedPages.Count > 0)
                {
                    isSavingPdf = true;
                    SetScanningState(false);

                    try
                    {
                        await SavePagesIndividuallyAsync(
                            completedPages);


                        int totalFiles = chkDuplex.Checked
                            ? completedPages.Count / 2
                            : completedPages.Count;

                        MessageBox.Show(
                            $"Đã tạo {totalFiles} file PDF.\n" +
                            (chkDuplex.Checked
                                ? "Mỗi file chứa 2 mặt của một tờ giấy."
                                : "Mỗi file chứa 1 mặt của một tờ giấy."),
                            "Scan Manager",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                    }
                    catch (Exception ex)
                    {
                        // Chỉ giữ các ảnh chưa được xóa,
                        // tránh đưa đường dẫn không tồn tại
                        // vào danh sách quản lý trang.
                        scannedPages.AddRange(
                            completedPages.Where(File.Exists));

                        MessageBox.Show(
                            "Lỗi lưu từng trang:\n" + ex.Message +
                            "\n\nCác ảnh chưa lưu vẫn được giữ lại.",
                            "Scan Manager",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    finally
                    {
                        isSavingPdf = false;
                        SetScanningState(false);
                    }

                    return;
                }

                // Chế độ quét Duplex thông thường:
                // giữ các trang để nhấn Hoàn tất PDF.
                scannedPages.AddRange(completedPages);

                MessageBox.Show(
                    $"Đã quét {completedPages.Count} trang.\n" +
                    $"Tổng số trang: {scannedPages.Count}",
                    "Scan Manager");

                return;
            }


            // Auto Batch: mỗi lượt quét có ảnh là một tập.
            if (completedPages.Count > 0)
            {
                try
                {
                    isSavingPdf = true;
                    SetScanningState(false);

                    try
                    {
                        await SaveAutoBatchPdfAsync(completedPages);
                        LogMemoryUsage( $"Đã lưu Auto Batch {completedPages.Count} trang");
                    }
                    finally
                    {
                        isSavingPdf = false;
                        SetScanningState(false);
                    }

                    // Chỉ đặt lại bộ đếm sau khi lưu thành công.
                    autoBatchRetryCount = 0;

                    Debug.WriteLine(
                        $"AUTO BATCH: Đã lưu {completedPages.Count} trang.");
                }
                catch (Exception ex)
                {
                    scannedPages.AddRange(completedPages);

                    autoBatchMode = false;
                    autoBatchStopping = true;
                    autoBatchTimer?.Stop();

                    SetScanningState(false);

                    MessageBox.Show(
                        "Không thể lưu PDF tự động:\n" +
                        ex.Message +
                        "\n\nCác ảnh đã nhận được giữ lại.",
                        "Lỗi Auto Batch",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    return;
                }
            }
            else
            {
                Debug.WriteLine(
                    "AUTO BATCH: Không có ảnh, không tạo PDF.");
            }

            // Nếu người dùng đã nhấn Dừng,
            // không bắt đầu tập tiếp theo.
            if (autoBatchStopping)
            {
                StopAutoBatchScan();
                return;
            }

            // Tiếp tục chờ tập tài liệu mới.
            if (autoBatchMode)
            {
                autoBatchTimer?.Start();
            }
        }

        private string CreateAutoBatchFolder()
        {
            string rootFolder = txtFolder.Text.Trim();

            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                throw new InvalidOperationException(
                    "Vui lòng chọn thư mục lưu tài liệu.");
            }

            string documentType = txtDocumentType.Text.Trim();

            if (string.IsNullOrWhiteSpace(documentType))
                documentType = "TaiLieu";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                documentType = documentType.Replace(c, '_');
            }

            string baseFolder = Path.Combine(
                rootFolder,
                documentType);

            Directory.CreateDirectory(baseFolder);

            while (true)
            {
                string folderName =
                    $"TaiLieu_{DateTime.Now:yyyyMMdd}_{autoBatchNumber:D3}";

                string folderPath = Path.Combine(
                    baseFolder,
                    folderName);

                autoBatchNumber++;

                if (Directory.Exists(folderPath))
                    continue;

                Directory.CreateDirectory(folderPath);

                return folderPath;
            }
        }

        private void TwainSession_SourceDisabled(
            object? sender,
            EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(FinishTwainScan));
                return;
            }

            FinishTwainScan();
        }


        private async Task SavePagesIndividuallyAsync(
            List<string> pages)
        {
            if (pages == null || pages.Count == 0)
                return;

            string documentType = txtDocumentType.Text.Trim();
            string filePrefix = txtFileName.Text.Trim();

            if (string.IsNullOrWhiteSpace(filePrefix))
                filePrefix = "To";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                filePrefix = filePrefix.Replace(c, '_');
            }

            // Duplex: 2 mặt quét = 1 tờ giấy.
            // Simplex: 1 mặt quét = 1 tờ giấy.
            bool isDuplex = chkDuplex.Checked;

            int pagesPerFile = isDuplex ? 2 : 1;

            // Kiểm tra đủ cặp mặt trước / mặt sau.
            if (isDuplex && pages.Count % 2 != 0)
            {
                throw new InvalidOperationException(
                    $"Quét hai mặt nhận được {pages.Count} ảnh.\n" +
                    "Số ảnh phải chia hết cho 2 để ghép " +
                    "mỗi tờ thành một PDF.\n" +
                    "Các ảnh gốc vẫn được giữ lại.");
            }

            // Tạo thư mục riêng cho mỗi lượt quét.
            string folderPath = CreateAutoBatchFolder();

            int totalFiles = pages.Count / pagesPerFile;
            int savedFiles = 0;

            for (int i = 0; i < pages.Count; i += pagesPerFile)
            {
                // Danh sách các mặt của cùng một tờ giấy.
                List<string> sheetPages = new();

                sheetPages.Add(pages[i]);

                if (isDuplex)
                {
                    // Mặt sau của tờ giấy.
                    sheetPages.Add(pages[i + 1]);
                }

                int sheetNumber = (i / pagesPerFile) + 1;

                string pdfName =
                    $"{filePrefix}_{sheetNumber:D3}.pdf";

                string pdfPath = Path.Combine(
                    folderPath,
                    pdfName);

                // Kiểm tra ảnh trước khi xuất PDF.
                foreach (string imagePath in sheetPages)
                {
                    if (!File.Exists(imagePath))
                    {
                        throw new FileNotFoundException(
                            "Không tìm thấy ảnh đã quét.",
                            imagePath);
                    }
                }

                // Mỗi tờ tạo thành một PDF riêng.
                // Duplex: PDF chứa 2 trang.
                // Simplex: PDF chứa 1 trang.
                await Task.Run(() =>
                    LowMemoryPdfWriter.CreatePdf(
                        sheetPages,
                        pdfPath));

                // Chỉ ghi lịch sử khi PDF đã tạo thành công.
                SaveDocument(
                    pdfName,
                    documentType,
                    pdfPath);

                lastPdfPath = pdfPath;

                // Chỉ xóa ảnh tạm sau khi đã lưu PDF
                // và ghi SQLite thành công.
                foreach (string imagePath in sheetPages)
                {
                    try
                    {
                        if (File.Exists(imagePath))
                            File.Delete(imagePath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "Không xóa được ảnh tạm: " + ex.Message);
                    }
                }

                savedFiles++;

                Debug.WriteLine(
                    $"Đã lưu {savedFiles}/{totalFiles} tờ: {pdfPath}");
            }

            // Chỉ tải lại lịch sử một lần.
            LoadDocuments();

            LogMemoryUsage(
                $"Đã lưu {savedFiles} tờ thành PDF riêng");
        }


        private async Task SaveAutoBatchPdfAsync(List<string> pages)
        {
            if (pages == null || pages.Count == 0)
                return;
            // Người dùng chọn mỗi trang một PDF.
            if (chkOnePageOneFile.Checked)
            {
                await SavePagesIndividuallyAsync(pages);
                return;
            }

            // Đọc control WinForms trên luồng giao diện.
            string documentType = txtDocumentType.Text.Trim();

            // Tạo thư mục riêng cho mỗi tập.
            string folderPath = CreateAutoBatchFolder();
            string folderName = Path.GetFileName(folderPath);

            string pdfPath = Path.Combine(
                folderPath,
                folderName + ".pdf");

            // Tạo PDF trên luồng nền, chỉ đọc ảnh từng trang.
            await Task.Run(() =>
                LowMemoryPdfWriter.CreatePdf(pages, pdfPath));

            // Sau await trở về luồng giao diện.
            SaveDocument(
                Path.GetFileName(pdfPath),
                documentType,
                pdfPath);

            lastPdfPath = pdfPath;

            LoadDocuments();

            // Chỉ xóa ảnh tạm sau khi PDF và SQLite thành công.
            foreach (string imagePath in pages)
            {
                try
                {
                    if (File.Exists(imagePath))
                        File.Delete(imagePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "Không xóa được ảnh tạm: " + ex.Message);
                }
            }

            Debug.WriteLine(
                $"AUTO BATCH: Đã lưu {pages.Count} trang: {pdfPath}");
        }


        private ScanOptions GetScanOptions()
        {
            int index = cmbScanner.SelectedIndex;

            if (index < 0 || index >= scannerDevices.Count)
                throw new InvalidOperationException(
                    "Vui lòng chọn máy scan.");

            return new ScanOptions
            {
                DeviceId = scannerDevices[index].DeviceID,
                Dpi = GetSelectedDpi(),

                ColorMode = cmbColorMode.SelectedIndex switch
                {
                    1 => 2, // Xám
                    2 => 4, // Đen trắng
                    _ => 1  // Màu
                },

                Duplex = chkDuplex.Checked
            };
        }

        private static WIA.Device ConnectScanner(string deviceId)
        {
            WIA.DeviceManager manager = new WIA.DeviceManager();

            foreach (WIA.DeviceInfo info in manager.DeviceInfos)
            {
                if (info.Type == WIA.WiaDeviceType.ScannerDeviceType &&
                    info.DeviceID == deviceId)
                {
                    return info.Connect();
                }
            }

            throw new InvalidOperationException(
                "Không tìm thấy máy scan đã chọn. Kiểm tra kết nối USB.");
        }


        private void CheckTwainScanner()
        {
            NTwain.TwainSession? session = null;

            try
            {
                var appId = NTwain.Data.TWIdentity.CreateFromAssembly(
                    NTwain.Data.DataGroups.Image,
                    typeof(ScanManagerForm).Assembly);

                session = new NTwain.TwainSession(appId);

                session.Open();

                var sources = session.GetSources().ToList();

                if (sources.Count == 0)
                {
                    MessageBox.Show(
                        "Không tìm thấy máy Scan qua TWAIN.\n" +
                        "Hãy kiểm tra driver TWAIN Canon.",
                        "Kiểm tra TWAIN");
                    return;
                }

                string names = string.Join(
                    Environment.NewLine,
                    sources.Select(x => x.Name));

                MessageBox.Show(
                    "TWAIN nhận diện được:\n\n" + names,
                    "Kiểm tra TWAIN");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Lỗi TWAIN:\n" + ex.Message,
                    "Scan Manager");
            }
            finally
            {
                session?.Close();
            }
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
                Debug.WriteLine(
                    "WIA: Lỗi giải phóng COM: " + ex.Message);
            }
        }


        protected override void OnFormClosing(
            FormClosingEventArgs e)
        {
            // Không cho thoát khi còn đang quét
            // hoặc đang tạo PDF trên luồng nền.
            if (isScanning || isSavingPdf)
            {
                e.Cancel = true;

                MessageBox.Show(
                    "Ứng dụng đang quét hoặc lưu PDF.\n" +
                    "Vui lòng hoàn tất lượt hiện tại trước khi thoát.",
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            // Dừng Auto Batch nếu đang chờ giấy.
            autoBatchStopping = true;
            autoBatchMode = false;
            stopScanRequested = true;

            if (autoBatchTimer != null)
            {
                autoBatchTimer.Stop();
                autoBatchTimer.Tick -= AutoBatchTimer_Tick;
                autoBatchTimer.Dispose();
                autoBatchTimer = null;
            }

            base.OnFormClosing(e);
        }

        private void LogMemoryUsage(string stage)
        {
            try
            {
                using Process process = Process.GetCurrentProcess();

                process.Refresh();

                double workingSetMb =
                    process.WorkingSet64 / 1024.0 / 1024.0;

                double privateMb =
                    process.PrivateMemorySize64 / 1024.0 / 1024.0;

                double managedMb =
                    GC.GetTotalMemory(false) / 1024.0 / 1024.0;

                Debug.WriteLine(
                    $"[RAM] {stage} | " +
                    $"Working Set: {workingSetMb:F1} MB | " +
                    $"Private: {privateMb:F1} MB | " +
                    $"Managed: {managedMb:F1} MB");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "Không đọc được RAM: " + ex.Message);
            }
        }


        private void txtDocumentType_TextChanged(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void lblDpi_Click(object sender, EventArgs e)
        {

        }

        private void lblScanner_Click(object sender, EventArgs e)
        {

        }
    }
}