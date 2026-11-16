using System;
using System.IO;
using System.Text;

namespace MeetingBell
{
    internal static class DataFile
    {
        public static string Write(string path, string content)
        {
            string temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return null;
            }
            catch (IOException error)
            {
                return error.Message;
            }
            catch (UnauthorizedAccessException error)
            {
                return error.Message;
            }
        }

        public static void SetAside(string path)
        {
            try
            {
                string broken = path + ".broken";
                if (File.Exists(broken)) File.Delete(broken);
                File.Move(path, broken);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
