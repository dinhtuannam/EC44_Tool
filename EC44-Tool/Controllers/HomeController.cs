using EC44_Tool.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System;

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

        [HttpGet]
        public IActionResult GetPostgresFileContent(string postgresPath, string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(postgresPath) || string.IsNullOrEmpty(fileName))
                {
                     return Json(new { success = false, message = "Đường dẫn Postgres hoặc tên file không được trống." });
                }

                string filePath = Path.Combine(postgresPath, fileName);

                if (!System.IO.File.Exists(filePath))
                {
                    // File does not exist in Postgres folder, return success with empty content
                    return Json(new { success = true, content = "", message = "File không tồn tại trong thư mục Postgres." });
                }

                // Read the file content, specifically handling UTF-8 for Japanese characters
                string fileContent = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);

                return Json(new { success = true, content = fileContent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Postgres file content for file: {FileName}", fileName);
                return Json(new { success = false, message = "Đã xảy ra lỗi khi đọc nội dung file Postgres: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult SaveFileAndImage([FromBody] SaveRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.PostgresPath) || string.IsNullOrEmpty(request.FileName))
                {
                    return Json(new { success = false, message = "Đường dẫn Postgres hoặc tên file không được trống." });
                }

                // --- Save Text File ---
                string postgresFilePath = Path.Combine(request.PostgresPath, request.FileName);
                // Ensure directory exists
                Directory.CreateDirectory(request.PostgresPath);
                // Write content, creating file if it doesn't exist, overwriting if it does
                System.IO.File.WriteAllText(postgresFilePath, request.FileContent, System.Text.Encoding.UTF8);

                // --- Save Image File (if provided) ---
                string imageMessage = "";
                if (!string.IsNullOrEmpty(request.ImagePath) && !string.IsNullOrEmpty(request.ImageData) && request.ImageData.StartsWith("data:image/"))
                {
                    try
                    {
                        // Extract base64 string (remove data:image/...;base64,)
                        string base64Data = request.ImageData.Split(',')[1];
                         byte[] imageBytes = Convert.FromBase64String(base64Data);

                        // Determine image extension (basic handling)
                        string extension = ".png"; // Default to png
                        if (request.ImageData.Contains("image/jpeg")) extension = ".jpg";
                        else if (request.ImageData.Contains("image/png")) extension = ".png";
                        else if (request.ImageData.Contains("image/gif")) extension = ".gif";

                        string imageFileName = Path.GetFileNameWithoutExtension(request.FileName) + extension; // Use base name from Oracle file
                        string imageFilePath = Path.Combine(request.ImagePath, imageFileName);

                         // Ensure directory exists
                        Directory.CreateDirectory(request.ImagePath);

                        // Save image file
                        System.IO.File.WriteAllBytes(imageFilePath, imageBytes);
                         imageMessage = $" và ảnh '{imageFileName}' ";

                    }
                    catch (Exception imgEx)
                    {
                         _logger.LogError(imgEx, "Error saving image file for file: {FileName}", request.FileName);
                         imageMessage = " (Lưu ảnh thất bại: " + imgEx.Message + ")";
                    }
                }

                // --- Determine New Status ---
                // Re-check if the file now exists in Postgres to update status on UI
                 bool postgresFileNowExists = System.IO.File.Exists(Path.Combine(request.PostgresPath, request.FileName));
                 FileStatus newStatus = postgresFileNowExists ? FileStatus.DaLam : FileStatus.ChuaLam;

                return Json(new { success = true, message = "Lưu file text" + imageMessage + "thành công!", newStatus = (int)newStatus });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving file and image for file: {FileName}", request.FileName);
                return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu file: " + ex.Message });
            }
        }

        // Helper class for SaveFileAndImage request body
        public class SaveRequest
        {
            public string FileName { get; set; } // Oracle file name
            public string FileContent { get; set; } // Content for Postgres file
            public string ImageData { get; set; } // Base64 image data
            public string PostgresPath { get; set; }
            public string ImagePath { get; set; }
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
                FilePath = Path.Combine(oraclePath, file) // FilePath refers to Oracle file for Copy buttons
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
                    return Json(new { success = false, message = "Đường dẫn file Oracle không hợp lệ hoặc file không tồn tại." });
                }

                // Read the file content, specifically handling UTF-8 for Japanese characters
                string fileContent = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);

                return Json(new { success = true, content = fileContent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Oracle file content for path: {FilePath}", filePath);
                return Json(new { success = false, message = "Đã xảy ra lỗi khi đọc nội dung file Oracle: " + ex.Message });
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
