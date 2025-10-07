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
			int? methodStartLine = null;
			int? methodEndLine = null;
			List<ParamUsage> paramUsages = new List<ParamUsage>();
			if (!string.IsNullOrWhiteSpace(request.MethodName))
			{
				var info = ExtractJavaMethodInfo(content, request.MethodName);
				if (info != null)
				{
					methodSource = info.Source;
					methodStartLine = info.StartLine;
					methodEndLine = info.EndLine;
					paramUsages = ExtractParamUsages(info.Source, constants);

					var paramMeta = ExtractParamMeta(info.Source);
					if (paramMeta.Count != paramUsages.Count)
					{
						return Json(new { success = false, message = $"Parameter count mismatch: found {paramMeta.Count} .param entries but {paramUsages.Count} SQL placeholders.", data = constants, methodSource, methodStartLine, methodEndLine, paramUsages });
					}

					for (int i = 0; i < paramUsages.Count; i++)
					{
						paramUsages[i].Param = paramMeta[i].Param;
						paramUsages[i].Type = paramMeta[i].Type;
					}
				}
			}

			return Json(new { success = true, data = constants, methodSource, methodStartLine, methodEndLine, paramUsages });
		}

		private MethodInfoResult ExtractJavaMethodInfo(string source, string methodName)
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
						// Compute 1-based line numbers
						int startLine = 1;
						for (int j = 0; j < startIndex; j++) if (source[j] == '\n') startLine++;
						int endLine = startLine;
						for (int j = startIndex; j < endIndex; j++) if (source[j] == '\n') endLine++;
						return new MethodInfoResult
						{
							Source = source.Substring(startIndex, endIndex - startIndex),
							StartLine = startLine,
							EndLine = endLine
						};
					}
				}
			}

			return null;
		}

		private List<ParamUsage> ExtractParamUsages(string methodSource, Dictionary<string, string> constants)
		{
			var results = new List<ParamUsage>();
			if (string.IsNullOrEmpty(methodSource)) return results;

			// Process per statement ended by ';'
			var statements = methodSource.Split(';');
			int idx = 1;
			foreach (var raw in statements)
			{
				var s = raw;
				if (string.IsNullOrWhiteSpace(s)) continue;
				if (!s.Contains(".append(") || !s.Contains("?")) continue;

				// collect args of each append in this statement
				var argRegex = new Regex(@"\.append\s*\((?<arg>[^)]*)\)");
				var parts = new List<string>();
				foreach (Match m in argRegex.Matches(s))
				{
					var arg = m.Groups["arg"].Value.Trim();
					if (arg.Length == 0) continue;
					if (arg.StartsWith("\"") && arg.EndsWith("\""))
					{
						// string literal
						var literal = arg.Substring(1, arg.Length - 2);
						parts.Add(literal);
					}
					else
					{
						// identifier/expression: map via constants if exact key
						var ident = arg;
						if (constants.TryGetValue(ident, out var mapped))
						{
							parts.Add($"<{mapped}>");
						}
						else
						{
							parts.Add(ident);
						}
					}
				}

				var text = string.Join(string.Empty, parts).Trim();
				if (text.Contains("?"))
				{
					results.Add(new ParamUsage { Index = idx++, Text = text });
				}
			}

			return results;
		}

		private List<(string Param, string Type)> ExtractParamMeta(string methodSource)
		{
			var list = new List<(string Param, string Type)>();
			if (string.IsNullOrEmpty(methodSource)) return list;
			// Match .param(i++, PARAM, TYPE)
			var rx = new Regex(@"\.param\s*\(\s*[^,]*,\s*(?<param>[^,]+),\s*(?<type>[^)]+)\)");
			foreach (Match m in rx.Matches(methodSource))
			{
				var param = m.Groups["param"].Value.Trim();
				var type = m.Groups["type"].Value.Trim();
				list.Add((param, type));
			}
			return list;
		}

		private class MethodInfoResult
		{
			public string Source { get; set; }
			public int StartLine { get; set; }
			public int EndLine { get; set; }
		}

		public class ParseRequest
		{
			public string FilePath { get; set; }
			public string MethodName { get; set; }
		}

		public class ParamUsage
		{
			public int Index { get; set; }
			public string Text { get; set; }
			public string Param { get; set; }
			public string Type { get; set; }
		}
	}
}

