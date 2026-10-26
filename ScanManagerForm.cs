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
using System.Drawing.Imaging;

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
            cmbFileType.SelectedIndexChanged += (s, e) =>
            {
                btnFinishPdf.Text =
                    cmbFileType.Text == "PDF"
                        ? "Hoàn tất PDF"
                        : "Hoàn tất file";
            };
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
            string fileType = cmbFileType.Text.Trim().ToUpperInvariant();

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

            List<string> pages = scannedPages.ToList();

            isSavingPdf = true;
            SetScanningState(false);

            try
            {
                string saveFolder = Path.Combine(
                    rootFolder,
                    documentType);

                int fileCount = await ExportAndRecordAsync(
                    pages,
                    saveFolder,
                    fileName,
                    fileType,
                    oneFilePerSheet: false,
                    duplex: chkDuplex.Checked,
                    documentType: documentType);

                scannedPages.Clear();

                MessageBox.Show(
                    $"Đã xuất {fileCount} file {fileType}.\n" +
                    $"Thư mục: {saveFolder}",
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Lỗi xuất file:\n" + ex.Message +
                    "\n\nCác ảnh gốc vẫn được giữ lại.",
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

            // Giá trị mặc định khi mở ứng dụng
            chkDuplex.Checked = false;
            chkContinuousScan.Checked = true;
            chkOnePageOneFile.Checked = false;

            // Checkbox: Mỗi trang 1 file PDF
            chkOnePageOneFile.Text = "Mỗi tờ 1 file";
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

        private static string GetOutputExtension(string type)
        {
            return type.ToUpperInvariant() switch
            {
                "PDF" => ".pdf",
                "JPEG" => ".jpg",
                "PNG" => ".png",
                "TIFF" => ".tiff",
                "BMP" => ".bmp",
                _ => throw new InvalidOperationException(
                    "Định dạng file không được hỗ trợ: " + type)
            };
        }

        private static string GetUniqueOutputPath(
            string folder, string name, string extension)
        {
            string path = Path.Combine(folder, name + extension);

            int number = 1;

            while (File.Exists(path))
            {
                path = Path.Combine(
                    folder,
                    $"{name}_{number++:D3}{extension}");
            }

            return path;
        }

        // Xuất một ảnh JPG, PNG hoặc BMP.
        private static void SaveSingleImage(
            string imagePath,
            string outputPath,
            string fileType)
        {
            string tempPath = outputPath + ".partial";

            try
            {
                string sourceExtension =
                    Path.GetExtension(imagePath).ToLowerInvariant();

                string targetExtension = GetOutputExtension(fileType);

                // Nếu đã đúng định dạng, giữ nguyên ảnh gốc
                // bằng cách copy, không mã hóa lại.
                bool sameFormat =
                    sourceExtension == targetExtension ||
                    (sourceExtension == ".jpeg" &&
                     targetExtension == ".jpg");

                if (sameFormat)
                {
                    File.Copy(imagePath, tempPath);
                }
                else
                {
                    using var image = Image.FromFile(imagePath);

                    switch (fileType)
                    {
                        case "JPEG":
                            SaveScanJpeg(image, tempPath);
                            break;

                        case "PNG":
                            image.Save(tempPath, ImageFormat.Png);
                            break;

                        case "BMP":
                            image.Save(tempPath, ImageFormat.Bmp);
                            break;

                        default:
                            throw new InvalidOperationException(
                                "Định dạng ảnh không hỗ trợ.");
                    }
                }

                File.Move(tempPath, outputPath);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }

                throw;
            }
        }

        // Xuất một hoặc nhiều trang thành TIFF.
        private static void SaveMultiPageTiff(
            IReadOnlyList<string> imagePaths,
            string outputPath)
        {
            if (imagePaths.Count == 0)
                throw new InvalidOperationException(
                    "Không có ảnh để tạo TIFF.");

            string tempPath = outputPath + ".partial";

            try
            {
                if (imagePaths.Count == 1)
                {
                    using var single = new Bitmap(imagePaths[0]);
                    single.Save(tempPath, ImageFormat.Tiff);
                }
                else
                {
                    var codec = ImageCodecInfo.GetImageEncoders()
                        .FirstOrDefault(x =>
                            x.MimeType == "image/tiff")
                        ?? throw new InvalidOperationException(
                            "Windows không có TIFF encoder.");

                    using var first = new Bitmap(imagePaths[0]);

                    using (var start = new EncoderParameters(1))
                    {
                        start.Param[0] = new EncoderParameter(
                            System.Drawing.Imaging.Encoder.SaveFlag,
                            (long)EncoderValue.MultiFrame);

                        first.Save(tempPath, codec, start);
                    }

                    for (int i = 1; i < imagePaths.Count; i++)
                    {
                        // Chỉ giữ thêm một trang ảnh trong RAM.
                        using var next = new Bitmap(imagePaths[i]);

                        using var add = new EncoderParameters(1);

                        add.Param[0] = new EncoderParameter(
                            System.Drawing.Imaging.Encoder.SaveFlag,
                            (long)EncoderValue.FrameDimensionPage);

                        first.SaveAdd(next, add);
                    }

                    using (var finish = new EncoderParameters(1))
                    {
                        finish.Param[0] = new EncoderParameter(
                            System.Drawing.Imaging.Encoder.SaveFlag,
                            (long)EncoderValue.Flush);

                        first.SaveAdd(finish);
                    }
                }

                File.Move(tempPath, outputPath);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }

                throw;
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
            cmbFileType.Enabled = !busy;

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

            // 1. Đóng TWAIN sau khi quét
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
                scannedPages.AddRange(
                    completedPages.Where(File.Exists));

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

            // 2. Kiểm tra lỗi truyền ảnh
            if (twainTransferFailed)
            {
                List<string> validPages = completedPages
                    .Where(File.Exists)
                    .ToList();

                scannedPages.AddRange(validPages);

                autoBatchMode = false;
                autoBatchStopping = true;
                autoBatchTimer?.Stop();

                SetScanningState(false);

                MessageBox.Show(
                    "Có lỗi trong quá trình nhận ảnh TWAIN:\n" +
                    twainTransferError +
                    $"\n\nĐã giữ lại {validPages.Count} trang." +
                    "\nVui lòng kiểm tra trước khi lưu.",
                    "Lỗi quét tài liệu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            // Lấy định dạng đã chọn
            string fileType = cmbFileType.Text
                .Trim()
                .ToUpperInvariant();

            // 3. Quét thông thường, KHÔNG dùng Auto Batch
            if (!autoBatchMode)
            {
                // Mỗi tờ giấy một file
                if (chkOnePageOneFile.Checked &&
                    completedPages.Count > 0)
                {
                    isSavingPdf = true;
                    SetScanningState(false);

                    try
                    {
                        // Hàm này đã hỗ trợ:
                        // PDF, JPEG, PNG, TIFF, BMP
                        int totalFiles =
                            await SavePagesIndividuallyAsync(
                                completedPages);

                        string description;

                        if (chkDuplex.Checked &&
                            (fileType == "PDF" ||
                             fileType == "TIFF"))
                        {
                            description =
                                "Mỗi file chứa 2 mặt của một tờ giấy.";
                        }
                        else if (chkDuplex.Checked)
                        {
                            description =
                                "Mỗi mặt giấy được lưu thành " +
                                "một file ảnh riêng.";
                        }
                        else
                        {
                            description =
                                "Mỗi tờ giấy được lưu thành một file.";
                        }

                        MessageBox.Show(
                            $"Đã tạo {totalFiles} file {fileType}.\n" +
                            description,
                            "Scan Manager",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        // Chỉ giữ các ảnh quét còn tồn tại.
                        scannedPages.AddRange(
                            completedPages.Where(File.Exists));

                        MessageBox.Show(
                            "Lỗi xuất file:\n" +
                            ex.Message +
                            "\n\nCác ảnh quét gốc còn tồn tại " +
                            "được giữ lại để xử lý.",
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

                // Quét bình thường:
                // Giữ các trang để người dùng
                // nhấn Hoàn tất rồi xuất theo định dạng đã chọn.
                scannedPages.AddRange(completedPages);

                MessageBox.Show(
                    $"Đã quét {completedPages.Count} trang.\n" +
                    $"Tổng số trang: {scannedPages.Count}\n\n" +
                    $"Định dạng xuất: {fileType}\n" +
                    "Nhấn Hoàn tất để lưu tài liệu.",
                    "Scan Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            // 4. AUTO BATCH
            if (completedPages.Count > 0)
            {
                isSavingPdf = true;
                SetScanningState(false);

                try
                {
                    // Tự động xuất theo cmbFileType
                    // và trạng thái chkOnePageOneFile.
                    await SaveAutoBatchPdfAsync(completedPages);

                    // Ghi RAM sau khi lưu xong mỗi tập
                    LogMemoryUsage(
                        $"Đã lưu Auto Batch " +
                        $"{completedPages.Count} trang - {fileType}");

                    // Chỉ reset retry sau khi xuất thành công
                    autoBatchRetryCount = 0;

                    Debug.WriteLine(
                        $"AUTO BATCH: Đã xuất " +
                        $"{completedPages.Count} trang ({fileType}).");
                }
                catch (Exception ex)
                {
                    // Giữ lại ảnh chưa bị xóa.
                    scannedPages.AddRange(
                        completedPages.Where(File.Exists));

                    autoBatchMode = false;
                    autoBatchStopping = true;
                    autoBatchTimer?.Stop();

                    MessageBox.Show(
                        $"Không thể xuất file {fileType}:\n" +
                        ex.Message +
                        "\n\nCác ảnh quét còn tồn tại " +
                        "được giữ lại để khôi phục.",
                        "Lỗi Auto Batch",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    return;
                }
                finally
                {
                    isSavingPdf = false;
                    SetScanningState(false);
                }
            }
            else
            {
                Debug.WriteLine(
                    "AUTO BATCH: Không có ảnh, " +
                    "không tạo file.");
            }

            // 5. Nếu đã nhấn Dừng
            if (autoBatchStopping)
            {
                StopAutoBatchScan();
                return;
            }

            // 6. Chờ tập tài liệu tiếp theo
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



        private async Task<int> SavePagesIndividuallyAsync(
            List<string> pages)
        {
            if (pages.Count == 0)
                return 0;

            string documentType = txtDocumentType.Text.Trim();
            string fileType = cmbFileType.Text.Trim().ToUpperInvariant();

            string prefix = txtFileName.Text.Trim();

            if (string.IsNullOrWhiteSpace(prefix))
                prefix = "To";

            bool duplex = chkDuplex.Checked;

            string folder = CreateAutoBatchFolder();

            return await ExportAndRecordAsync(
                pages,
                folder,
                prefix,
                fileType,
                oneFilePerSheet: true,
                duplex: duplex,
                documentType: documentType);
        }




        private async Task SaveAutoBatchPdfAsync(
            List<string> pages)
        {
            if (pages.Count == 0)
                return;

            // Mỗi tờ một file: PDF/TIFF ghép mặt;
            // JPEG/PNG/BMP xuất riêng từng mặt.
            if (chkOnePageOneFile.Checked)
            {
                await SavePagesIndividuallyAsync(pages);
                return;
            }

            string documentType = txtDocumentType.Text.Trim();
            string fileType = cmbFileType.Text.Trim().ToUpperInvariant();

            string folder = CreateAutoBatchFolder();

            string prefix = Path.GetFileName(folder);

            await ExportAndRecordAsync(
                pages,
                folder,
                prefix,
                fileType,
                oneFilePerSheet: false,
                duplex: chkDuplex.Checked,
                documentType: documentType);
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

        private static List<string> ExportScanFiles(
            IReadOnlyList<string> pages,
            string folder,
            string prefix,
            string fileType,
            bool oneFilePerSheet,
            bool duplex)
        {
            if (pages.Count == 0)
                throw new InvalidOperationException(
                    "Không có trang để xuất file.");

            fileType = fileType.Trim().ToUpperInvariant();

            string extension = GetOutputExtension(fileType);

            bool multiPageType =
                fileType == "PDF" || fileType == "TIFF";

            if (duplex && pages.Count % 2 != 0)
            {
                throw new InvalidOperationException(
                    $"Quét Duplex nhận được {pages.Count} mặt.\n" +
                    "Số mặt không chia hết cho 2.\n" +
                    "Vui lòng kiểm tra giấy bị kẹt hoặc thiếu mặt.\n" +
                    "Ảnh quét gốc vẫn được giữ lại.");
            }

            Directory.CreateDirectory(folder);

            List<string> outputFiles = new();

            void ExportGroup(
                IReadOnlyList<string> group,
                string outputName)
            {
                foreach (string path in group)
                {
                    if (!File.Exists(path))
                        throw new FileNotFoundException(
                            "Thiếu ảnh quét.", path);
                }

                string outputPath = GetUniqueOutputPath(
                    folder, outputName, extension);

                switch (fileType)
                {
                    case "PDF":
                        CreatePdfFromScanImages(
                            group, outputPath);
                        break;

                    case "TIFF":
                        SaveMultiPageTiff(
                            group, outputPath);
                        break;

                    case "JPEG":
                    case "PNG":
                    case "BMP":
                        if (group.Count != 1)
                            throw new InvalidOperationException(
                                "Định dạng ảnh chỉ hỗ trợ một mặt mỗi file.");

                        SaveSingleImage(
                            group[0], outputPath, fileType);
                        break;
                }

                outputFiles.Add(outputPath);
            }

            try
            {
                // PDF/TIFF thông thường: một tập thành một file.
                if (multiPageType && !oneFilePerSheet)
                {
                    ExportGroup(pages, prefix);
                }
                // PDF/TIFF mỗi tờ một file.
                else if (multiPageType)
                {
                    int groupSize = duplex ? 2 : 1;

                    for (int i = 0; i < pages.Count; i += groupSize)
                    {
                        string name =
                            $"{prefix}_{i / groupSize + 1:D3}";

                        var group = pages
                            .Skip(i)
                            .Take(groupSize)
                            .ToArray();

                        ExportGroup(group, name);
                    }
                }
                // JPEG, PNG, BMP: mỗi mặt một file ảnh.
                else
                {
                    for (int i = 0; i < pages.Count; i++)
                    {
                        string name;

                        if (duplex)
                        {
                            int sheet = i / 2 + 1;
                            string side =
                                i % 2 == 0 ? "Truoc" : "Sau";

                            name = $"{prefix}_{sheet:D3}_{side}";
                        }
                        else
                        {
                            name = $"{prefix}_{i + 1:D3}";
                        }

                        ExportGroup(
                            new[] { pages[i] }, name);
                    }
                }

                return outputFiles;
            }
            catch
            {
                // Chưa xóa ảnh quét gốc.
                // Xóa những output chưa ghi vào SQLite
                // nếu quá trình tạo file thất bại.
                foreach (string path in outputFiles)
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch { }
                }

                throw;
            }
        }

        private void SaveDocumentsBatch(
            IReadOnlyList<string> files,
            string documentType)
        {
            using var conn =
                new SqliteConnection($"Data Source={dbPath}");

            conn.Open();

            using var transaction = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();

            cmd.Transaction = transaction;

            cmd.CommandText = @"
        INSERT INTO Documents
        (FileName, DocumentType, FilePath, CreatedDate)
        VALUES (@name, @type, @path, @date)";

            var nameParam =
                cmd.Parameters.Add("@name", SqliteType.Text);

            var typeParam =
                cmd.Parameters.Add("@type", SqliteType.Text);

            var pathParam =
                cmd.Parameters.Add("@path", SqliteType.Text);

            var dateParam =
                cmd.Parameters.Add("@date", SqliteType.Text);

            typeParam.Value = documentType;

            dateParam.Value =
                DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            foreach (string file in files)
            {
                nameParam.Value = Path.GetFileName(file);
                pathParam.Value = file;

                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        private async Task<int> ExportAndRecordAsync(
            List<string> pages,
            string folder,
            string prefix,
            string fileType,
            bool oneFilePerSheet,
            bool duplex,
            string documentType)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                prefix = prefix.Replace(c, '_');
            }

            if (string.IsNullOrWhiteSpace(prefix))
                prefix = "TaiLieu";

            // Chạy toàn bộ xử lý ảnh ở luồng nền.
            List<string> outputFiles = await Task.Run(() =>
                ExportScanFiles(
                    pages,
                    folder,
                    prefix,
                    fileType,
                    oneFilePerSheet,
                    duplex));

            // Quay về UI thread, ghi lịch sử một lần.
            SaveDocumentsBatch(outputFiles, documentType);

            lastPdfPath = outputFiles.LastOrDefault();

            // Chỉ xóa ảnh gốc khi file đầu ra và
            // giao dịch SQLite đều thành công.
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

            LoadDocuments();

            LogMemoryUsage(
                $"Đã xuất {outputFiles.Count} file {fileType}");

            return outputFiles.Count;
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