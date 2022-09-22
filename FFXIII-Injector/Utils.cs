using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Net;
using System.Diagnostics;
using System.Reflection;

namespace FFXIII_Injector
{
    internal static class Utils
    {
        public static byte[] ArrayReverse(byte[] arr)
        {
            Array.Reverse(arr);
            return arr;
        }
        public static void DecryptFilelist(string file)
        {
            var info = new FileInfo(file);
            RunFfxiiiCrypt("-d \"" + info.FullName + "\" filelist");
        }

        public static void EncryptFilelist(string file)
        {
            var info = new FileInfo(file);
            RunFfxiiiCrypt("-e \"" + info.FullName + "\" filelist");
        }
        public static void WriteChecksum(string file, int address)
        {
            var info = new FileInfo(file);
            string args = "-c \"" + info.FullName + "\" " + address.ToString("x8") + " write";
            RunFfxiiiCrypt(args);
        }
        public static void RunFfxiiiCrypt(string args)
        {
            FileInfo fileInfo = new FileInfo(Assembly.GetExecutingAssembly().Location);
            ProcessStartInfo processStartInfo = new ProcessStartInfo();
            processStartInfo.FileName = fileInfo.DirectoryName + "\\Utilities\\ffxiiicrypt.exe";
            processStartInfo.Arguments = " " + args;
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process process = Process.Start(processStartInfo))
            {
                process.WaitForExit();
            }
        }
    }
}
