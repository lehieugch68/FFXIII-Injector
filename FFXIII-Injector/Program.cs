using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace FFXIII_Injector
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Injector.Inject(@"D:\VietHoaGame\FF13-2\mod\Utilities\filelist_scrc.win32.bin", "");
            Console.ReadKey();
        }
    }
}
