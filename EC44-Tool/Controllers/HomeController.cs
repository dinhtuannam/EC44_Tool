using EC44_Tool.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace EC44_Tool.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult GetFileList(string oraclePath, string postgresPath, string nameList, string filter)
        {
            try
            {
                if (string.IsNullOrEmpty(oraclePath) || string.IsNullOrEmpty(postgresPath))
                {
                    return Json(new { success = false, message = "Vui lòng nhập đầy đủ đường dẫn Oracle và Postgres" });
                }

                var oracleFiles = GetTxtFileNames(oraclePath);
                var postgresFiles = GetTxtFileNames(postgresPath);

                var desiredNames = ParseNameList(nameList);

                var filteredOracleFiles = FilterFilesByBaseName(oracleFiles, desiredNames);

                var fileList = CreateFileListModels(filteredOracleFiles, postgresFiles, oraclePath);

                if (!string.IsNullOrEmpty(filter))
                {
                    fileList = fileList.Where(f => f.Status.ToString().Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                return Json(new { success = true, data = fileList });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file list.");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi xử lý yêu cầu: " + ex.Message });
            }
        }

        private List<string> GetTxtFileNames(string directoryPath)
        {
            if (Directory.Exists(directoryPath))
            {
                return Directory.GetFiles(directoryPath, "*.txt")
                                .Select(Path.GetFileName)
                                .ToList();
            }
            return new List<string>();
        }

        private List<string> ParseNameList(string nameList)
        {
            return string.IsNullOrEmpty(nameList) ? new List<string>() : nameList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private List<string> FilterFilesByBaseName(List<string> fileNames, List<string> desiredNames)
        {
            if (!desiredNames.Any())
            {
                return fileNames; // If no desired names, return all files
            }

            return fileNames.Where(file => 
            {
                var baseName = GetBaseFileName(file);
                return desiredNames.Any(name => name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        }

         private List<FileListModel> CreateFileListModels(List<string> oracleFiles, List<string> postgresFiles, string oraclePath)
        {
             return oracleFiles.Select(file => new FileListModel
            {
                FileName = file,
                Status = postgresFiles.Contains(file) ? FileStatus.DaLam : FileStatus.ChuaLam,
                FilePath = Path.Combine(oraclePath, file)
            }).ToList();
        }

        private string GetBaseFileName(string fileName)
        {
            var match = Regex.Match(fileName, @"^(.*?)(_\d+|_txt)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(fileName);
        }

        [HttpGet]
        public IActionResult GetFileContent(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                {
                    return Json(new { success = false, message = "Đường dẫn file không hợp lệ hoặc file không tồn tại." });
                }

                // Read the file content, specifically handling UTF-8 for Japanese characters
                string fileContent = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);

                return Json(new { success = true, content = fileContent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file content for path: {FilePath}", filePath);
                return Json(new { success = false, message = "Đã xảy ra lỗi khi đọc nội dung file: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetCombinedFileContent(string oraclePath, string postgresPath, string fileName)
        {
            try
            {
                string oracleFilePath = Path.Combine(oraclePath, fileName);
                string postgresFilePath = Path.Combine(postgresPath, fileName);

                if (!System.IO.File.Exists(oracleFilePath) || !System.IO.File.Exists(postgresFilePath))
                {
                    return Json(new { success = false, message = "Không tìm thấy cả hai file Oracle và Postgres." });
                }

                // Read content of both files with UTF-8 encoding
                string oracleContent = System.IO.File.ReadAllText(oracleFilePath, System.Text.Encoding.UTF8);
                string postgresContent = System.IO.File.ReadAllText(postgresFilePath, System.Text.Encoding.UTF8);

                // Combine content (you can customize the format here if needed)
                string combinedContent = $"Oracle Content:\n{oracleContent}\n\nPostgres Content:\n{postgresContent}";

                return Json(new { success = true, content = combinedContent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting combined file content for file: {FileName}", fileName);
                return Json(new { success = false, message = "Đã xảy ra lỗi khi lấy nội dung kết hợp: " + ex.Message });
            }
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
