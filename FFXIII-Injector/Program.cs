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
            //filelist white repackdir
            if (args.Length >= 3)
            {
                Injector.Inject(args[0], args[1], args[2]);
            }
            Console.ReadKey();
        }
    }
}
