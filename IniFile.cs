using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoIO
{
    public static class RegexExtensions
    {
        public static string ReplaceGroup(
            this Regex regex, string input, string groupName, string replacement)
        {
            return regex.Replace(
                input,
                m =>
                {
                    var group = m.Groups[groupName];
                    var sb = new StringBuilder();
                    var previousCaptureEnd = 0;
                    foreach (var capture in group.Captures.Cast<Capture>())
                    {
                        var currentCaptureEnd =
                            capture.Index + capture.Length - m.Index;
                        var currentCaptureLength =
                            capture.Index - m.Index - previousCaptureEnd;
                        sb.Append(
                            m.Value.Substring(
                                previousCaptureEnd, currentCaptureLength));
                        sb.Append(replacement);
                        previousCaptureEnd = currentCaptureEnd;
                    }
                    sb.Append(m.Value.Substring(previousCaptureEnd));

                    return sb.ToString();
                });
        }
    }

    public interface IniElementContainer
    {
        void Add(IniFile.IniElement elem);

        void Remove(IniFile.IniElement elem);
        
        IniFile.IniElement this[int index]
        { get; }
        
        int Count();
    }

    public class IniFile : IniElementContainer
    {
        public class IniElement
        {
            static Regex pattern = new Regex(@"^.*?(;(?<comment>.*))*$", RegexOptions.Compiled);

            protected string _raw = null;
            protected int _rawSize = -1;
            protected bool _rawHasComment = false;
            protected string _comment = null;
            protected bool _dirty = false;
            protected IniElementContainer _containner = null;

            public IniElement(string line = null)
            {
                if (line != null)
                {
                    _raw = line;
                    _rawSize = line.Length;

                    Match match = pattern.Match(line);
                    if ((_rawHasComment = match.Groups["comment"].Success) == true)
                        _comment = match.Groups["comment"].Value;
                }
            }

            public IniElement(string line, Match match)
            {
                _raw = line;
                _rawSize = line.Length;

                if ((_rawHasComment = match.Groups["comment"].Success) == true)
                    _comment = match.Groups["comment"].Value;
            }

            public void Remove()
            {
                if (Containner != null)
                {
                    Containner.Remove(this);
                    this.Containner = null;
                }
                else
                    throw new InvalidOperationException("Cannot remove ini element without containner!");
            }

            public string Comment
            {
                get => _comment;
                set
                {
                    if (value != null)
                    {
                        _dirty = true;
                        _comment = value;
                    }
                    else
                        throw new ArgumentNullException("assignment not nullable! consider using RemoveComment()");
                }
            }

            public void RemoveComment()
            {
                this._comment = null;
            }

            public IniElementContainer Containner
            {
                get => _containner;
                set
                {
                    _dirty = true;
                    _containner = value;
                }
            }

            virtual public void Write(StreamWriter sw)
            {
                if (_rawHasComment)
                {
                    if (Comment == null)
                    {
                        _raw = pattern.ReplaceGroup(_raw, "comment", "");
                        _raw = _raw.Substring(0, _raw.Length - 1);
                        _rawSize = _raw.Length;
                        _rawHasComment = false;
                    }
                    else
                    {
                        _raw = pattern.ReplaceGroup(_raw, "comment", Comment);
                        _rawSize = _raw.Length;
                    }
                }
                else
                {
                    if (Comment != null)
                    {
                        _raw += " ;" + Comment;
                        _rawSize = _raw.Length;
                        _rawHasComment = true;
                    }
                }
                sw.WriteLine(_raw);
            }
        }

        public class IniProperty : IniElement
        {
            static public bool CommentAsValue = false;
            static public Regex pattern
            {
                get
                {
                    if (CommentAsValue)
                        return new Regex(@"^\s*(?<key>.+?)\s*=\s*(?<value>.+?)\s*$", RegexOptions.Compiled);
                    else
                        return new Regex(@"^\s*(?<key>.+?)\s*=\s*(?<value>.+?)\s*(;(?<comment>.*))*$", RegexOptions.Compiled);
                }
            }

            static public bool TryGetInstance(string line, out IniElement elem)
            {
                Match match = pattern.Match(line);
                if (match.Success)
                {
                    elem = new IniProperty(line, match);
                    return true;
                }

                elem = null;
                return false;
            }

            private string _name = null;
            private string _value = null;

            public string Name
            {
                get => _name;
                set
                {
                    if (value != null)
                    {
                        _dirty = true;
                        _name = value;
                    }
                    else
                        throw new ArgumentNullException("assignment not nullable!");
                }
            }
            public string Value
            {
                get => _value;
                set
                {
                    if (value != null)
                    {
                        _dirty = true;
                        _value = value;
                    }
                    else
                        throw new ArgumentNullException("assignment not nullable!");
                }
            }

            public IniProperty(string line, Match succeed) : base(line, succeed)
            {
                _name = succeed.Groups["key"].Value;
                _value = succeed.Groups["value"].Value;
            }

            override public void Write(StreamWriter sw)
            {
                _raw = pattern.ReplaceGroup(_raw, "key", Name);
                _raw = pattern.ReplaceGroup(_raw, "value", Value);
                _rawSize = _raw.Length;
                base.Write(sw);
                //_dirty = false;
            }
        }

        public class IniSection : IniElement, IniElementContainer
        {

            static Regex pattern = new Regex(@"^\s*\[(?<section>.+?)\]\s*(;(?<comment>.*))*$", RegexOptions.Compiled);
            
            static public bool TryGetInstance(string line, out IniElement elem)
            {
                Match match = pattern.Match(line);
                if (match.Success)
                {
                    elem = new IniSection(line, match);

                    return true;
                }

                elem = null;
                return false;
            }

            public void Add(IniElement elem)
            {
                this._elements.Add(elem);
                elem.Containner = this;
            }

            public void Remove(IniFile.IniElement elem)
            {
                this._elements.Remove(elem);
                elem.Containner = null;
            }

            public bool ContainProperty(string name)
            {
                return this._elements.Any(prop => prop as IniProperty != null && (prop as IniProperty).Name == name);
            }

            public IniElement this[int index]
            {
                get
                {
                    return _elements[index];
                }
            }

            public int Count()
            {
                return _elements.Count();
            }

            internal List<IniElement> _elements = new List<IniElement>();

            private string _name = null;
            public string Name
            {
                get => _name;
                set
                {
                    if (value != null)
                    {
                        _dirty = true;
                        _name = value;
                    }
                    else
                        throw new ArgumentNullException("assignment not nullable!");
                }
            }
            
            public IniSection(string line, Match succeed) : base (line, succeed)
            {
                _name = succeed.Groups["section"].Value;
            }

            public IniProperty this[string key]
            {
                get
                {
                    foreach (int i in Enumerable.Range(0, _elements.Count()).Reverse())
                    {
                        if (_elements[i] is IniProperty && (_elements[i] as IniProperty).Name == key)
                            return _elements[i] as IniProperty;
                    }
                    
                    throw new KeyNotFoundException();
                }
            }
            
            private IEnumerable<IniProperty> Properties()
            {
                foreach (IniElement elem in _elements)
                {
                    if (elem is IniProperty)
                        yield return elem as IniProperty;
                }
            }

            public IEnumerable<IniElement> Elements()
            {
                return _elements;
            }

            override public void Write(StreamWriter sw)
            {
                _raw = pattern.ReplaceGroup(_raw, "section", Name);
                _rawSize = _raw.Length;
                base.Write(sw);

                foreach (IniElement elem in _elements)
                    elem.Write(sw);

                //_dirty = false;
            }
        }
        
        private List<IniElement> _elements = new List<IniElement>();
        private string _iniFile = null;

        public void Add(IniElement elem)
        {
            this._elements.Add(elem);
            elem.Containner = this;
        }

        public void Remove(IniFile.IniElement elem)
        {
            this._elements.Remove(elem);
            elem.Containner = null;
        }

        public bool ContainProperty(string name)
        {
            return this._elements.Any(prop => prop as IniProperty != null && (prop as IniProperty).Name == name);
        }

        public bool ContainSection(string name)
        {
            return this._elements.Any(prop => prop as IniSection != null && (prop as IniSection).Name == name);
        }

        public IniElement this[int index]
        {
            get
            {
                return _elements[index];
            }
        }

        public int Count()
        {
            return _elements.Count();
        }

        public IniFile(string inifile)
        {
            if (File.Exists(inifile))
            {
                _iniFile = Path.GetFullPath(inifile);

                IniElementContainer container = this;
                IniElement elem = null;

                using (StreamReader sr = File.OpenText(_iniFile))
                {
                    string line = String.Empty;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (IniSection.TryGetInstance(line, out elem))
                        {
                            this.Add(elem);
                            container = elem as IniElementContainer;
                        }
                        else
                        {
                            container.Add(IniProperty.TryGetInstance(line, out elem) ?
                                elem : new IniElement(line));
                        }
                    }
                }
            }
        }

        public IniSection this[string key]
        {
            get
            {
                foreach (int i in Enumerable.Range(0, _elements.Count()).Reverse())
                {
                    if (_elements[i] is IniSection && (_elements[i] as IniSection).Name == key)
                        return _elements[i] as IniSection;
                }

                throw new KeyNotFoundException();
            }
        }

        public IEnumerable<IniSection> Sections()
        {
            foreach (IniElement elem in _elements)
            {
                if (elem is IniSection)
                    yield return elem as IniSection;
            }
        }
        public IEnumerable<IniProperty> Properties()
        {
            foreach (IniElement elem in _elements)
            {
                if (elem is IniProperty)
                    yield return elem as IniProperty;
            }
        }
        public IEnumerable<IniElement> Elements()
        {
            return _elements;
        }
        
        public void Write(StreamWriter sw)
        {
            foreach (IniElement elem in _elements)
                elem.Write(sw);
        }
    }

    static class Program
    {
        [STAThread]
        static int Main()
        {
            IniFile.IniProperty.CommentAsValue = false;
            IniFile ini = new IniFile(@"..\..\..\test.ini");
            
            foreach (IniFile.IniProperty props in ini.Properties())
                System.Console.WriteLine(props.Name + ":" + props.Value);

            System.Console.WriteLine("");

            System.Console.WriteLine(ini["WinNT50P_English"]["Title"].Value);


            if (ini.ContainSection("WinNT50P_English"))
                if (ini["WinNT50P_English"].ContainProperty("test"))
                    System.Console.WriteLine(ini["WinNT50P_English"]["test"].Value);


            ini["WinNT50P_English"]["Title"].Name = "test";
            ini[0].RemoveComment();
            using (StreamWriter sw = new StreamWriter(@"..\..\..\test2.ini"))
                ini.Write(sw);


            System.Console.ReadLine();
            return 0;
        }
    }
}
