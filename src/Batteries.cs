using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class DesktopBatteryReader {
 [StructLayout(LayoutKind.Sequential)] struct Info { public int size; public Guid classId; public uint instance; public IntPtr reserved; }
 [StructLayout(LayoutKind.Sequential)] struct Key { public Guid guid; public uint id; public Key(string g,uint i){guid=new Guid(g);id=i;} }
 public class Reading { public string name; public int percent; }
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevsW(IntPtr c,string e,IntPtr p,uint f);
 [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInfo(IntPtr h,uint i,ref Info d);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] static extern bool SetupDiGetDevicePropertyW(IntPtr h,ref Info d,ref Key k,out uint t,byte[] b,uint n,out uint needed,uint flags);
 [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
 static byte[] Read(IntPtr h,ref Info d,Key k){uint t,n;byte[] b=new byte[2048];if(!SetupDiGetDevicePropertyW(h,ref d,ref k,out t,b,(uint)b.Length,out n,0))return null;Array.Resize(ref b,(int)n);return b;}
 public static Reading[] Get(){var items=new List<Reading>();var h=SetupDiGetClassDevsW(IntPtr.Zero,null,IntPtr.Zero,6);if(h==new IntPtr(-1))return items.ToArray();try{for(uint i=0;;i++){var d=new Info();d.size=Marshal.SizeOf(d);if(!SetupDiEnumDeviceInfo(h,i,ref d))break;var b=Read(h,ref d,new Key("104ea319-6ee2-4701-bd47-8ddbf425bbe5",2));if(b==null||b.Length!=1||b[0]>100)continue;var name=Read(h,ref d,new Key("a45c254e-df1c-4efd-8020-67d146a850e0",14))??Read(h,ref d,new Key("a45c254e-df1c-4efd-8020-67d146a850e0",2));if(name!=null)items.Add(new Reading{name=Encoding.Unicode.GetString(name).TrimEnd('\0'),percent=b[0]});}}finally{SetupDiDestroyDeviceInfoList(h);}return items.ToArray();}
}