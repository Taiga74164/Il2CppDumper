namespace Il2CppDumper
{
    public class StringLiteralRecord
    {
        public uint index { get; set; }
        public string value { get; set; } = string.Empty;
        public string address { get; set; } = "0x0";
        public int dataIndex { get; set; }
        public int length { get; set; }
    }
}
