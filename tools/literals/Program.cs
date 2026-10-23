using System;
using LibCpp2IL;
using AssetRipper.Primitives;
class P {
  static void Main(string[] a) {
    LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();
    if (!LibCpp2IlMain.LoadFromFile(a[0], a[1], UnityVersion.Parse("6000.0.74f1"))) { Console.WriteLine("load failed"); return; }
    for (int i = 2; i < a.Length; i++) {
      ulong va = 0x180000000UL + Convert.ToUInt64(a[i], 16);
      string s = null;
      try { s = LibCpp2IlMain.GetLiteralByAddress(va); } catch (Exception e) { s = "ERR " + e.Message; }
      Console.WriteLine($"{a[i]} => {s ?? "(null)"}");
    }
  }
}
