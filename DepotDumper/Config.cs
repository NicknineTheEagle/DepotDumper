using System;
using System.Collections.Generic;
using System.Text;

namespace DepotDumper
{
    class Config
    {
        public static bool RememberPassword = false;
        public static string SuppliedPassword = null;
        public static bool DumpUnreleased = false;
        public static uint TargetAppId = uint.MaxValue;
    }
}
