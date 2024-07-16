using Google.Protobuf.WellKnownTypes;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyResult
    {
        public long LastInsertedId { get; }
        public long RowsAffected { get; }
        
        public PolyphenyResult(long lastInsertedId, long rowsAffected)
        {
            this.LastInsertedId = lastInsertedId;
            this.RowsAffected = rowsAffected;
        }

        public static PolyphenyResult Empty()
        {
            return new PolyphenyResult(-1, -1);
        }
    }
}