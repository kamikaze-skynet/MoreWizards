using System;
using System.IO;
using System.Linq;
using LibCpp2IL;
using AssetRipper.Primitives;
class P {
  static void Main(string[] a) {
    LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();
    LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = false;
    if (!LibCpp2IlMain.LoadFromFile(a[0], a[1], UnityVersion.Parse("6000.0.74f1"))) { Console.WriteLine("load failed"); return; }
    var bin = LibCpp2IlMain.Binary;
    using var w = new StreamWriter(a[2]);
    int n = 0;
    foreach (var m in LibCpp2IlMain.TheMetadata.methodDefs) {
      ulong va = m.MethodPointer;
      if (va == 0) continue;
      ulong rva = va - 0x180000000UL; // GameAssembly.dll image base
      var t = m.DeclaringType;
      w.WriteLine($"{rva:X}\t{t?.FullName}\t{m.Name}\t{m.ReturnType}");
      n++;
    }
    Console.WriteLine($"wrote {n}");
  }
}
