using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Communication.Ports
{

    public class PortSettings
    {
        public string Name { get; set; } = string.Empty;

        public virtual string GetID()
        {
            return string.Format("AbstratPort|{0}", Name);
        }
    }

    /// <summary>
    /// Abstract class for port communication implements IPort interface
    /// </summary>
    ///
    public abstract class AbstractPort : IPort
    {
        /// <summary>
        /// Gets or sets the read data buffer list.
        /// </summary>
        /// <value>The read data buffer list.</value>
        public List<byte> ReadData { get; set; } = new();

        /// <summary>
        /// Gets or sets the settings.
        /// </summary>
        /// <value>The settings.</value>
        public PortSettings Settings { get; set; } = null!;

        /// <summary>
        /// Gets or sets the process data delegate.
        /// </summary>
        /// <value>The process data delegate.</value>
        private event ProcessDataDelegate? OnProcessData;

        /// <summary>
        /// Gets or sets a value indicating whether port is open.
        /// </summary>
        /// <value><c>true</c> if this port is open; otherwise, <c>false</c>.</value>
        protected bool _isOpen { get; set; } = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbstractPort"/> class.
        /// </summary>
        public AbstractPort()
        {
            ReadData = new List<Byte>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AbstractPort"/> class.
        /// </summary>
        /// <param name="settings">The settings.</param>
        public AbstractPort(PortSettings settings) : this()
        {
            Settings = settings;
        }

        public abstract void Write(byte[] data);

        public abstract byte[] Read(int size);

        public abstract void Open();

        public abstract void Close();

        public abstract bool HasReadData(int size);

        public abstract byte[] RemoveReadData(int size);

        public virtual bool IsOpen()
        {
            return _isOpen;
        }

        public virtual void Clear()
        {
            ReadData.Clear();
        }

        public virtual bool IsEmpty()
        {
            return (ReadData.Count == 0);
        }

        public virtual byte[] GetData()
        {
            return ReadData.ToArray();
        }

        public void SubscribeProcessData(ProcessDataDelegate action)
        {
            OnProcessData += action;
        }

        public void UnSubscribeProcessData(ProcessDataDelegate action)
        {
            OnProcessData -= action;
        }

        public void InvokeProcessData()
        {
            OnProcessData?.Invoke();
        }
    }
}
