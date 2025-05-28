namespace EC44_Tool.Models
{
    public enum FileStatus
    {
        ChuaLam,
        DaLam
    }

    public class FileListModel
    {
        public string FileName { get; set; }
        public FileStatus Status { get; set; }
        public string FilePath { get; set; }
    }
} 