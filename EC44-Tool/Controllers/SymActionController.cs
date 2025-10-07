using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using System.Text;

namespace EC44_Tool.Controllers
{
	public class SymActionController : Controller
	{
		public IActionResult Index()
		{
			return View();
		}

		[HttpPost]
		public IActionResult ParseConstants([FromBody] ParseRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.FilePath))
			{
				return Json(new { success = false, message = "Missing file path" });
			}

			if (!System.IO.File.Exists(request.FilePath))
			{
				return Json(new { success = false, message = "File does not exist" });
			}

			string content;
			try
			{
				content = System.IO.File.ReadAllText(request.FilePath, Encoding.UTF8);
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Unable to read file: {ex.Message}" });
			}

			// Regex for lines like: private static final String NAME = "VALUE";
			var constantPattern = @"private\s+static\s+final\s+String\s+(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*""(?<value>[^""]*)""\s*;";
			var constantRegex = new Regex(constantPattern);
			var constants = new Dictionary<string, string>();
			foreach (Match m in constantRegex.Matches(content))
			{
				var key = m.Groups["key"].Value;
				var value = m.Groups["value"].Value;
				if (!constants.ContainsKey(key))
				{
					constants[key] = value;
				}
			}

			string methodSource = null;
			if (!string.IsNullOrWhiteSpace(request.MethodName))
			{
				methodSource = ExtractJavaMethod(content, request.MethodName);
			}

			return Json(new { success = true, data = constants, methodSource });
		}

		private string ExtractJavaMethod(string source, string methodName)
		{
			// Match modifiers, optional static, generic/array return types, parameters, opening brace
			var signaturePattern = $@"(?m)(public|protected|private)\s+(?:static\s+)?[\w\.<>,\[\]\s]+\s+{Regex.Escape(methodName)}\s*\([^)]*\)\s*\{{";
			var sigRegex = new Regex(signaturePattern);
			var match = sigRegex.Match(source);
			if (!match.Success)
			{
				return null;
			}

			int startIndex = match.Index;
			int braceStart = source.IndexOf('{', match.Index + match.Length - 1);
			if (braceStart == -1)
			{
				return null;
			}

			int depth = 0;
			bool inString = false;
			bool inChar = false;
			bool inLineComment = false;
			bool inBlockComment = false;
			for (int i = braceStart; i < source.Length; i++)
			{
				char c = source[i];
				char next = i + 1 < source.Length ? source[i + 1] : '\0';

				if (inLineComment)
				{
					if (c == '\n') inLineComment = false;
					continue;
				}
				if (inBlockComment)
				{
					if (c == '*' && next == '/') { inBlockComment = false; i++; }
					continue;
				}
				if (!inString && !inChar && c == '/' && next == '/') { inLineComment = true; i++; continue; }
				if (!inString && !inChar && c == '/' && next == '*') { inBlockComment = true; i++; continue; }

				if (!inChar && c == '"') { inString = !inString; continue; }
				if (!inString && c == '\'') { inChar = !inChar; continue; }

				if (inString || inChar) continue;

				if (c == '{') depth++;
				else if (c == '}')
				{
					depth--;
					if (depth == 0)
					{
						int endIndex = i + 1;
						return source.Substring(startIndex, endIndex - startIndex);
					}
				}
			}

			return null;
		}

		public class ParseRequest
		{
			public string FilePath { get; set; }
			public string MethodName { get; set; }
		}
	}
}

