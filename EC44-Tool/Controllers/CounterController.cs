using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace EC44_Tool.Controllers
{
    public class CounterController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult GetFileCount(string folderPath, string filterPrefix, string sortBy, string sortOrder)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return Json(new { success = false, message = "Đường dẫn không hợp lệ" });
            }

            try
            {
                var files = Directory.GetFiles(folderPath, "*.*")
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .ToList();

                // Apply filter if filterPrefix is provided
                if (!string.IsNullOrEmpty(filterPrefix))
                {
                    files = files.Where(f => {
                         // Filter based on the prefix before the first underscore
                         var firstUnderscoreIndex = f.IndexOf('_');
                         if (firstUnderscoreIndex > 0) {
                            return f.Substring(0, firstUnderscoreIndex).StartsWith(filterPrefix, StringComparison.OrdinalIgnoreCase);
                         }
                         return f.StartsWith(filterPrefix, StringComparison.OrdinalIgnoreCase); // Handle files without underscore
                    }).ToList();
                }

                var fileGroups = files
                    .GroupBy(f => {
                         var firstUnderscoreIndex = f.IndexOf('_');
                         if (firstUnderscoreIndex > 0)
                         {
                             return f.Substring(0, firstUnderscoreIndex);
                         }
                         return f; // Handle files without underscore
                    })
                    .Select(g => new
                    {
                        prefix = g.Key,
                        count = g.Count(),
                        files = g.ToList()
                    });

                // Apply sorting
                if (sortBy == "count")
                {
                    fileGroups = (sortOrder == "desc") ? fileGroups.OrderByDescending(g => g.count) : fileGroups.OrderBy(g => g.count);
                } else if (sortBy == "prefix")
                {
                     fileGroups = (sortOrder == "desc") ? fileGroups.OrderByDescending(g => g.prefix) : fileGroups.OrderBy(g => g.prefix);
                }
                // Default sort within groups (for modal) is by filename ascending

                return Json(new { success = true, data = fileGroups.ToList() });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
