using System;
using System.IO;
using System.Runtime.InteropServices;
using IFilterTextReader.Exceptions;

namespace IFilterTextReader
{
    /// <summary>
    /// This class implements a TextReader that reads from an IFilter 
    /// </summary>
    public class FilterReader : TextReader
    {
        #region Fields
        /// <summary>
        /// The IFilter interface
        /// </summary>
        private readonly NativeMethods.IFilter _filter;

        /// <summary>
        /// Contains the chunk that we are reading from the <see cref="NativeMethods.IFilter"/> interface
        /// </summary>
        private NativeMethods.STAT_CHUNK _currentChunk;

        /// <summary>
        /// Indicates that we are done with reading
        /// </summary>
        private bool _done;

        /// <summary>
        /// Indicates if the current chunk is valid
        /// </summary>
        private bool _currentChunkValid;

        /// <summary>
        /// Holds the chars that are left from the last chunck read
        /// </summary>
        private char[] _charsLeftFromLastRead;

        /// <summary>
        /// The file to read with an IFilter
        /// </summary>
        private readonly string _fileName;
        #endregion

        #region Constructor en Destructor
        /// <summary>
        /// Creates an TextReader object for the given <see cref="fileName"/>
        /// </summary>
        /// <param name="fileName"></param>
        public FilterReader(string fileName)
        {
            _fileName = fileName;
            _filter = FilterLoader.LoadAndInitIFilter(fileName);
            if (_filter == null)
                throw new IFFilterNotFound("There is no IFilter installed for the file '" + Path.GetFileName(fileName) + "'");
        }
        
        ~FilterReader()
        {
            Dispose(false);
        }

        #endregion

        #region Close
        public override void Close()
        {
            Dispose(true);
        }
        #endregion

        #region Dispose
        /// <summary>
        /// Disposes this object
        /// </summary>
        /// <param name="disposing"></param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        protected override void Dispose(bool disposing)
        {
            if (_filter != null)
                Marshal.ReleaseComObject(_filter);
            
            FilterLoader.Dispose();

            base.Dispose(true);

            GC.SuppressFinalize(this);
        }
        #endregion

        /// <summary>
        /// Read
        /// </summary>
        /// <returns></returns>
        public override string ReadLine()
        {
            var line = base.ReadLine();
            return line;
        }

        #region Read
        /// <summary>
        /// Overrides the standard <see cref="TextReader"/> read method
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="index"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public override int Read(char[] buffer, int index, int count)
        {
            var endOfChunksCount = 0;
            var charsRead = 0;

            while (!_done && charsRead < count)
            {
                if (_charsLeftFromLastRead != null)
                {
                    var charsToCopy = (_charsLeftFromLastRead.Length < count - charsRead)
                        ? _charsLeftFromLastRead.Length
                        : count - charsRead;

                    Array.Copy(_charsLeftFromLastRead, 0, buffer, index + charsRead, charsToCopy);

                    charsRead += charsToCopy;

                    if (charsToCopy < _charsLeftFromLastRead.Length)
                    {
                        var temp = new char[_charsLeftFromLastRead.Length - charsToCopy];
                        Array.Copy(_charsLeftFromLastRead, charsToCopy, temp, 0, temp.Length);
                        _charsLeftFromLastRead = temp;
                    }
                    else
                        _charsLeftFromLastRead = null;

                    continue;
                }

                if (!_currentChunkValid)
                {
                    var result = _filter.GetChunk(out _currentChunk);
                    _currentChunkValid = (result == NativeMethods.IFilterReturnCode.S_OK) &&
                                         ((_currentChunk.flags & NativeMethods.CHUNKSTATE.CHUNK_TEXT) != 0);

                    if (result == NativeMethods.IFilterReturnCode.FILTER_E_END_OF_CHUNKS)
                        endOfChunksCount++;

                    // There are no more chunks
                    if (endOfChunksCount > 1)
                        _done = true;
                }

                if (_currentChunkValid)
                {
                    var bufLength = (uint) (count - charsRead);
                    if (bufLength < 4096)
                        bufLength = 4096; //Read ahead

                    var buf = new char[bufLength];
                    var result = _filter.GetText(ref bufLength, buf);

                    switch (result)
                    {
                        case NativeMethods.IFilterReturnCode.FILTER_E_PASSWORD:
                            throw new IFFileIsPasswordProtected("The file '" + _fileName +
                                                                "' or a file inside this file (e.g. in the case of a ZIP) is password protected");

                        case NativeMethods.IFilterReturnCode.E_ACCESSDENIED:
                        case NativeMethods.IFilterReturnCode.FILTER_E_ACCESS:
                            throw new IFAccesFailure("Unable to acces the IFilter or file");

                        case NativeMethods.IFilterReturnCode.E_OUTOFMEMORY:
                            throw new OutOfMemoryException("Not enough memory to proceed reading the file '" + _fileName +
                                                           "'");

                        case NativeMethods.IFilterReturnCode.FILTER_E_UNKNOWNFORMAT:
                            throw new IFUnknownFormat("The file '" + _fileName +
                                                      "' is not in the format the IFilter would expect it to be");

                        case NativeMethods.IFilterReturnCode.FILTER_S_LAST_TEXT:
                        case NativeMethods.IFilterReturnCode.S_OK:
                            var read = (int) bufLength;
                            if (read + charsRead > count)
                            {
                                var charsLeft = (read + charsRead - count);
                                _charsLeftFromLastRead = new char[charsLeft];
                                Array.Copy(buf, read - charsLeft, _charsLeftFromLastRead, 0, charsLeft);
                                read -= charsLeft;
                            }
                            else
                                _charsLeftFromLastRead = null;

                            for (var i = 0; i < read; i ++)
                            {
                                switch (buf[i])
                                {
                                    case '\0':
                                        buf[i] = '\n';
                                        break;
                                }
                            }

                            Array.Copy(buf, 0, buffer, index + charsRead, read+1);
                            charsRead += read;

                            break;
                    }

                    if (result == NativeMethods.IFilterReturnCode.FILTER_S_LAST_TEXT ||
                        result == NativeMethods.IFilterReturnCode.FILTER_E_NO_MORE_TEXT)
                        _currentChunkValid = false;
                }
            }

            return charsRead;
        }
        #endregion
    }
}
