using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyParameterCollection: DbParameterCollection
    {
        private readonly List<PolyphenyParameter> _parameters = new List<PolyphenyParameter>();
        
        public static PolyphenyParameterCollection FromParameterMetas(IEnumerable<ParameterMeta> metas)
        {
            var collection = new PolyphenyParameterCollection();
            collection.Clear();
            foreach (var meta in metas)
            {
                var parameter = new PolyphenyParameter
                {
                    ParameterName = meta.Name,
                    TypeName = meta.TypeName,
                    Precision = meta.Precision,
                    Scale = meta.Scale,
                    PlaceholderName = meta.ParameterName
                };
                collection.Add(parameter);
            }

            return collection;
        }

        public void FillParameterValues(object[] values)
        {
            if (values.Length != _parameters.Count)
            {
                throw new ArgumentException("values must have the same length as the parameter collection");
            }
            
            for (var i = 0; i < values.Length; i++)
            {
                _parameters[i].Value = values[i];
            }
        }

        public IEnumerable<ProtoValue> ToProtoValues()
        {
            return this._parameters.Select(x => ProtoHelper.ToProtoValue(x.Value));
        }
        
        public override int Add(object value)
        {
            if (value is not PolyphenyParameter parameter)
            {
                throw new ArgumentException("value must be a PolyphenyParameter");
            }

            return this.Add(parameter);
        }

        private int Add(PolyphenyParameter value)
        {
            _parameters.Add(value);
            return _parameters.Count - 1;
        }

        public override void Clear()
        {
            this._parameters.Clear();
        }

        public override bool Contains(object value)
        {
            if (value is not PolyphenyParameter parameter)
            {
                throw new ArgumentException("value must be a PolyphenyParameter");
            }

            return this._parameters.Contains(parameter);
        }

        public override int IndexOf(object value)
        {
            if (value is not PolyphenyParameter parameter)
            {
                throw new ArgumentException("value must be a PolyphenyParameter");
            }

            return this._parameters.IndexOf(parameter);
        }

        public override void Insert(int index, object value)
        {
            if (value is not PolyphenyParameter parameter)
            {
                throw new ArgumentException("value must be a PolyphenyParameter");
            }

            this._parameters.Insert(index, parameter);
        }

        public override void Remove(object value)
        {
            if (value is not PolyphenyParameter parameter)
            {
                throw new ArgumentException("value must be a PolyphenyParameter");
            }

            this._parameters.Remove(parameter);
        }

        public override void RemoveAt(int index)
        {
            this._parameters.RemoveAt(index);
        }

        public override void RemoveAt(string parameterName)
        {
            this.RemoveAt(this.IndexOf(parameterName));
        }

        protected override void SetParameter(int index, DbParameter value)
        {
            if (index < 0 || index >= this._parameters.Count)
            {
                throw new IndexOutOfRangeException();
            }
            
            if (value is not PolyphenyParameter parameter)
            {
                throw new ArgumentException("value must be a PolyphenyParameter");
            }
            
            this._parameters[index] = parameter;
        }

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            this.SetParameter(this.IndexOf(parameterName), value);
        }

        public override int Count => this._parameters.Count;
        public override object SyncRoot => (this._parameters as IList).SyncRoot;

        public override int IndexOf(string parameterName)
        {
            for (var i = 0; i < this._parameters.Count; i++)
            {
                if (this._parameters[i].ParameterName == parameterName)
                {
                    return i;
                }
            }

            return -1;
        }

        public override bool Contains(string parameterName)
        {
            return this.IndexOf(parameterName) != -1;
        }

        public override void CopyTo(Array array, int index)
        {
            this._parameters.ToArray().CopyTo(array, index);
        }

        public override IEnumerator GetEnumerator()
        {
            return this._parameters.GetEnumerator();
        }

        protected override DbParameter GetParameter(int index)
        {
            return this._parameters[index];
        }

        protected override DbParameter GetParameter(string parameterName)
        {
            return this.GetParameter(this.IndexOf(parameterName));
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                this.Add(value);
            }
        }
    }
}