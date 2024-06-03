using System.Data;
using System.Data.Common;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyParameter: DbParameter
    {
        public override void ResetDbType()
        {
            throw new System.NotImplementedException();
        }

        public override DbType DbType { get; set; } = DbType.String;
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; }
        public override string SourceColumn { get; set; }
        public override object Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        
        // custom attributes
        public string TypeName { get; set; }
        public new int Precision { get; set; }
        public new int Scale { get; set; }
        public string PlaceholderName { get; set; }
    }
}