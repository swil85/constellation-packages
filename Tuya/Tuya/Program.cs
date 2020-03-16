using Constellation.Package;
using System;

namespace Tuya
{
    public class Program : PackageBase
    {
        static void Main(string[] args)
        {
            PackageHost.Start<Program>(args);
        }

        public override void OnStart()
        {
            //https://github.com/eppz/.NET.Library.TuyaKit
            //https://github.com/codetheweb/tuyapi
            //https://github.com/Marcus-L/m4rcus.TuyaCore
            
            PackageHost.WriteInfo("Package starting - IsRunning: {0} - IsConnected: {1}", PackageHost.IsRunning, PackageHost.IsConnected);

            //https://github.com/codetheweb/tuyapi/blob/master/docs/SETUP.md
            var plug = new TuyaPlug()
            {
                IP = "192.168.0.106",
                LocalKey = "5f5f784cd82d449b",
                Id = "0120015260091453a970"
            };

            Console.WriteLine($"Device[{plug.Id}] status: {plug.GetStatus().Result.Powered}");

            plug.SetStatus(true).Wait();
            Console.WriteLine($"Device[{plug.Id}] status: {plug.GetStatus().Result.Powered}");

            plug.SetStatus(false).Wait();
            Console.WriteLine($"Device[{plug.Id}] status: {plug.GetStatus().Result.Powered}");
        }
    }
}
