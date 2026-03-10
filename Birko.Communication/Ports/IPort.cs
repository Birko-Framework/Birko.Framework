using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Communication.Ports
{

    /// <summary>
    /// delegate called after the port has sended data
    /// </summary>
    public delegate void ProcessDataDelegate();

    public interface IPort
    {
        /// <summary>
        /// Writes the specified data.
        /// </summary>
        /// <param name="data">The data as byte array.</param>
        void Write(byte[] data);

        /// <summary>
        /// Reads the specified size fom port.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <returns>Byte array witdh data</returns>
        byte[] Read(int size);

        /// <summary>
        /// Opens port instance.
        /// </summary>
        void Open();

        /// <summary>
        /// Closes port instance.
        /// </summary>
        void Close();

        /// <summary>
        /// Determines whether port [has read data] [the specified size].
        /// </summary>
        /// <param name="size">The size.</param>
        /// <returns>
        /// 	<c>true</c> if port [has read data] [the specified size]; otherwise, <c>false</c>.
        /// </returns>
        bool HasReadData(int size);

        /// <summary>
        /// Removes the read data from port data buffer.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <returns>Removed data as byte array</returns>
        byte[] RemoveReadData(int size);

        bool IsOpen();

        void Clear();

        bool IsEmpty();

        byte[] GetData();

        void SubscribeProcessData(ProcessDataDelegate action);
        void UnSubscribeProcessData(ProcessDataDelegate action);
    }
}
