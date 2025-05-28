using EC44_Tool.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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

                var oracleFiles = Directory.GetFiles(oraclePath, "*.txt")
                    .Select(Path.GetFileName)
                    .ToList();

                var postgresFiles = Directory.GetFiles(postgresPath, "*.txt")
                    .Select(Path.GetFileName)
                    .ToList();

                var desiredNames = string.IsNullOrEmpty(nameList) ? new List<string>() : nameList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                var fileList = oracleFiles
                    .Where(file => 
                    {
                        if (desiredNames.Any())
                        {
                            var baseName = GetBaseFileName(file);
                            return desiredNames.Any(name => name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
                        }
                        // If nameList is empty, include all files from oraclePath
                        return true;
                    })
                    .Select(file => new FileListModel
                {
                    FileName = file,
                    Status = postgresFiles.Contains(file) ? "Đã làm" : "Chưa làm"
                }).ToList();

                if (!string.IsNullOrEmpty(filter))
                {
                    fileList = fileList.Where(f => f.Status == filter).ToList();
                }

                return Json(new { success = true, data = fileList });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string GetBaseFileName(string fileName)
        {
            // Use regex to extract the base name (part before the first _ followed by a digit)
            var match = Regex.Match(fileName, @"^(.*?)(_\d+|_txt)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(fileName);
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
