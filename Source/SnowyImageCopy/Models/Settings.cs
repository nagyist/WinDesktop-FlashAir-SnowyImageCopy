﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;

using SnowyImageCopy.Common;
using SnowyImageCopy.Helper;
using SnowyImageCopy.Models.ImageFile;

namespace SnowyImageCopy.Models
{
	/// <summary>
	/// This application's settings
	/// </summary>
	public class Settings : NotificationObject, INotifyDataErrorInfo
	{
		#region INotifyDataErrorInfo

		/// <summary>
		/// Holder of property name (key) and validation error messages (value)
		/// </summary>
		private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();

		public bool HasErrors => _errors.Any();

		public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

		public IEnumerable GetErrors(string propertyName)
		{
			if (string.IsNullOrEmpty(propertyName) || !_errors.ContainsKey(propertyName))
				return null;

			return _errors[propertyName];
		}

		private bool ValidateProperty(object value, [CallerMemberName] string propertyName = null)
		{
			if (string.IsNullOrEmpty(propertyName))
				return false;

			var context = new ValidationContext(this) { MemberName = propertyName };
			var results = new List<ValidationResult>();
			var isValidated = Validator.TryValidateProperty(value, context, results);

			if (isValidated)
			{
				if (_errors.ContainsKey(propertyName))
					_errors.Remove(propertyName);
			}
			else
			{
				if (_errors.ContainsKey(propertyName))
					_errors[propertyName].Clear();
				else
					_errors[propertyName] = new List<string>();

				_errors[propertyName].AddRange(results.Select(x => x.ErrorMessage));
			}

			RaiseErrorsChanged(propertyName);
			return isValidated;
		}

		private void RaiseErrorsChanged(string propertyName) =>
			ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));

		#endregion

		#region Settings

		#region Path

		public string RemoteAddress
		{
			get => _remoteAddress;
			set
			{
				if (_remoteAddress == value)
					return;

				if (!TryParseRemoteAddress(value, out var root, out var descendant, out var name))
					return;

				_remoteAddress = root + descendant;
				_remoteRoot = root;
				_remoteDescendant = string.IsNullOrEmpty(descendant) ? null : "/" + descendant.TrimEnd('/');
				_remoteName = name;
				RaisePropertyChanged();
				RaisePropertyChanged(nameof(RemoteName));
			}
		}
		private string _remoteAddress = @"http://flashair/"; // Default FlashAir address

		public string RemoteRoot => _remoteRoot ?? _remoteAddress;
		private string _remoteRoot;

		public string RemoteDescendant => _remoteDescendant ?? string.Empty;
		private string _remoteDescendant;

		internal (string remoteRoot, string remoteDescendant) RemoteRootDescendant => (RemoteRoot, RemoteDescendant);

		public string RemoteName => _remoteName ?? "flashair"; // Default FlashAir name
		private string _remoteName;

		private bool TryParseRemoteAddress(string source, out string root, out string descendant, out string name)
		{
			root = null;
			descendant = null;
			name = null;

			if (string.IsNullOrEmpty(source) || Path.GetInvalidPathChars().Any(x => source.Contains(x)))
				return false;

			var match = new Regex(@"^https?://(?<name>((?!/)\S){1,15})/").Match(source);
			if (!match.Success)
				return false;

			root = match.Value;

			descendant = source.Substring(match.Length).TrimStart('/');
			descendant = Regex.Replace(descendant, @"\s+", string.Empty);
			descendant = Regex.Replace(descendant, "/{2,}", "/");

			name = match.Groups["name"].Value; // Port number may be included.
			return true;
		}

		private const string DefaultLocalFolder = "FlashAirImages"; // Default local folder name

		public string LocalFolder
		{
			get
			{
				if (string.IsNullOrEmpty(_localFolder))
					_localFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), DefaultLocalFolder);

				return _localFolder;
			}
			set
			{
				string buffer;
				if (string.IsNullOrEmpty(value))
				{
					buffer = value;
				}
				else if (!PathAddition.TryNormalizePath(value, out buffer))
					return;

				SetPropertyValue(ref _localFolder, buffer);
				CheckLocalFolderValid();
			}
		}
		private string _localFolder;

		[XmlIgnore]
		public bool IsLocalFolderValid
		{
			get => _isLocalFolderValid;
			private set => SetPropertyValue(ref _isLocalFolderValid, value);
		}
		private bool _isLocalFolderValid = true;

		internal bool CheckLocalFolderValid()
		{
			return IsLocalFolderValid = Directory.Exists(Path.GetPathRoot(LocalFolder));
		}

		#endregion

		#region Date

		public FilePeriod TargetPeriod
		{
			get => _targetPeriod;
			set => SetPropertyValue(ref _targetPeriod, value);
		}
		private FilePeriod _targetPeriod = FilePeriod.All; // Default

		public ObservableCollection<DateTime> TargetDates
		{
			get => _targetDates ??= new ObservableCollection<DateTime>();
			set
			{
				if ((_targetDates != null) && (_targetDates == value))
					return;

				_targetDates = value;

				if (TargetPeriod == FilePeriod.Select) // To prevent loading settings from firing event unnecessarily
					RaisePropertyChanged();
			}
		}
		private ObservableCollection<DateTime> _targetDates;

		#endregion

		#region Current image

		public bool IsCurrentImageVisible
		{
			get => _isCurrentImageVisible;
			set => SetPropertyValue(ref _isCurrentImageVisible, value);
		}
		private bool _isCurrentImageVisible;

		public double CurrentImageWidth
		{
			get => _currentImageWidth;
			set
			{
				if (value < ImageManager.ThumbnailSize.Width)
					return;

				_currentImageWidth = value;
				RaisePropertyChanged();
			}
		}
		private double _currentImageWidth = ImageManager.ThumbnailSize.Width;

		#endregion

		#region Dashboard

		public bool CopyOnSelect
		{
			get => _copyOnSelect;
			set => SetPropertyValue(ref _copyOnSelect, value);
		}
		private bool _copyOnSelect = true; // Default

		public bool AutoCheckAtStart
		{
			get => _autoCheckAtStart;
			set => SetPropertyValue(ref _autoCheckAtStart, value);
		}
		private bool _autoCheckAtStart;

		#endregion

		#region Connection

		// XmlSerializer cannot work with TimeSpan.
		public double AutoCheckInterval
		{
			get => _autoCheckInterval;
			set => SetPropertyValue(ref _autoCheckInterval, value);
		}
		private double _autoCheckInterval = 30D; // Default

		public int TimeoutDuration
		{
			get => _timeoutDuration;
			set => SetPropertyValue(ref _timeoutDuration, value);
		}
		private int _timeoutDuration = 10; // Default

		#endregion

		#region File

		public bool OrdersFromNewer
		{
			get => !FileItem.OrderByAscendingDate;
			set
			{
				if (FileItem.OrderByAscendingDate != value)
					return;

				FileItem.OrderByAscendingDate = !value;
				RaisePropertyChanged();
			}
		}

		public bool MakesFileExtensionLowercase
		{
			get => _makesFileExtensionLowercase;
			set => SetPropertyValue(ref _makesFileExtensionLowercase, value);
		}
		private bool _makesFileExtensionLowercase = true; // Default

		#region Dated folder

		public bool CreatesDatedFolder
		{
			get => _createsDatedFolder;
			set => SetPropertyValue(ref _createsDatedFolder, value);
		}
		private bool _createsDatedFolder = true; // Default;

		[XmlIgnore]
		public bool CustomizesDatedFolder
		{
			get => _customizesDatedFolder || (CustomDatedFolder != null);
			set
			{
				if (_customizesDatedFolder == value)
					return;

				_customizesDatedFolder = value;
				if (!value)
					CustomDatedFolder = null;
			}
		}
		private bool _customizesDatedFolder;

		[XmlIgnore]
		public string DatedFolder
		{
			get => CustomDatedFolder ?? DefaultDatedFolder;
			set
			{
				var buffer = value?.Trim();
				if (string.IsNullOrEmpty(buffer) ||
					string.Equals(buffer, DefaultDatedFolder, StringComparison.OrdinalIgnoreCase))
				{
					CustomDatedFolder = null;
				}
				else if (IsDatedFolderValid(buffer))
				{
					CustomDatedFolder = buffer;
				}
			}
		}

		public string CustomDatedFolder
		{
			get => _customDatedFolder;
			set
			{
				SetPropertyValue(ref _customDatedFolder, value);
				RaisePropertyChanged(nameof(DatedFolder));
			}
		}
		private string _customDatedFolder;

		private const string DefaultDatedFolder = "yyyyMMdd";

		private static bool IsDatedFolderValid(string format)
		{
			// year:  y{1,4}
			// month: M{1,4}
			// day:   d{1,2}
			// delimiter: [-_]?
			return new Regex("^(?:y{1,4}[-_]?M{1,4}(?:|[-_]?d{1,2})|(?:|d{1,2}[-_]?)M{1,4}[-_]?y{1,4})$")
				.IsMatch(format ?? string.Empty);
		}

		#endregion

		public bool HandlesJpegFileOnly
		{
			get => _handlesJpegFileOnly;
			set => SetPropertyValue(ref _handlesJpegFileOnly, value);
		}
		private bool _handlesJpegFileOnly;

		public bool SelectsReadOnlyFile
		{
			get => _selectsReadOnlyFile;
			set => SetPropertyValue(ref _selectsReadOnlyFile, value);
		}
		private bool _selectsReadOnlyFile;

		public bool SkipsOnceCopiedFile
		{
			get => _skipsOnceCopiedFile;
			set
			{
				SetPropertyValue(ref _skipsOnceCopiedFile, value);

				if (!value)
					Signatures.Clear();
			}
		}
		private bool _skipsOnceCopiedFile;

		public int OnceCopiedCapacity
		{
			get => Signatures.MaxCount;
			set
			{
				if (Signatures.MaxCount == value)
					return;

				Signatures.MaxCount = value;
				RaisePropertyChanged();
			}
		}

		public bool LeavesExistingFile
		{
			get => _leavesExistingFile;
			set => SetPropertyValue(ref _leavesExistingFile, value);
		}
		private bool _leavesExistingFile;

		public bool MovesFileToRecycle
		{
			get => _movesFileToRecycle;
			set => SetPropertyValue(ref _movesFileToRecycle, value);
		}
		private bool _movesFileToRecycle;

		public bool DeletesOnCopy
		{
			get => _deletesOnCopy;
			set => SetPropertyValue(ref _deletesOnCopy, value);
		}
		private bool _deletesOnCopy;

		#endregion

		#region Sound

		public bool PlaysSound
		{
			get => SoundManager.PlaysSound;
			set
			{
				if (SoundManager.PlaysSound == value)
					return;

				SoundManager.PlaysSound = value;
				RaisePropertyChanged();
			}
		}

		public string CopyStarted
		{
			get => SoundManager.CopyStarted.Path;
			set
			{
				SoundManager.CopyStarted.Path = value;
				RaisePropertyChanged();
			}
		}

		public void TestCopyStarted() => SoundManager.CopyStarted.Play();

		public string OneCopied
		{
			get => SoundManager.OneCopied.Path;
			set
			{
				SoundManager.OneCopied.Path = value;
				RaisePropertyChanged();
			}
		}

		public void TestOneCopied() => SoundManager.OneCopied.Play();

		public string AllCopied
		{
			get => SoundManager.AllCopied.Path;
			set
			{
				SoundManager.AllCopied.Path = value;
				RaisePropertyChanged();
			}
		}

		public void TestAllCopied() => SoundManager.AllCopied.Play();

		#endregion

		#region Culture

		// A name of static event for data binding must meet the following rule:
		// https://docs.microsoft.com/en-us/dotnet/framework/wpf/getting-started/whats-new?#binding-to-static-properties
		public static event EventHandler CommonCultureNameChanged;

		public static string CommonCultureName
		{
			get => ResourceService.Current.CultureName;
			set
			{
				// If culture name is empty, Culture of this application's Resources will be automatically selected.
				ResourceService.Current.ChangeCulture(value);

				CommonCultureNameChanged?.Invoke(null, EventArgs.Empty);
			}
		}

		public string CultureName
		{
			get => _cultureName;
			set => SetPropertyValue(ref _cultureName, value);
		}
		private string _cultureName;

		#endregion

		#endregion

		[XmlIgnore]
		public int Index { get; private set; } = 0;
		internal string IndexString => (0 < Index) ? Index.ToString() : string.Empty;

		[XmlIgnore]
		public DateTime LastWriteTime
		{
			get => _lastWriteTime;
			private set => SetPropertyValue(ref _lastWriteTime, value);
		}
		private DateTime _lastWriteTime;

		public string Reserve { get; set; }

		public Settings()
		{ }

		public Settings(int index)
		{
			this.Index = index;
		}

		private CompositeDisposable _subscription;

		public void Start()
		{
			_subscription = new CompositeDisposable
			{
				Observable.FromEvent<PropertyChangedEventHandler, PropertyChangedEventArgs>
					(
						handler => (sender, e) => handler(e),
						handler => PropertyChanged += handler,
						handler => PropertyChanged -= handler
					)
					.Throttle(TimeSpan.FromSeconds(1))
					.Subscribe(_ =>
					{
						Save(this);
						LastWriteTime = DateTime.Now;
					}),

				Observable.FromEventPattern(typeof(Settings), nameof(CommonCultureNameChanged))
					.Subscribe(x => CultureName = CommonCultureName)
			};

			CultureName = CommonCultureName;
		}

		public void Stop()
		{
			_subscription?.Dispose();
			_subscription = null;
		}

		#region Load/Save/Delete

		private static string GetSettingsFileName(in string value) => $"settings{value}.xml";
		private static string GetSettingsFilePath(in string value) => FolderService.GetAppDataFilePath(GetSettingsFileName(value));

		public static IEnumerable<Settings> Load()
		{
			var folderPath = FolderService.AppDataFolderPath;
			if (!Directory.Exists(folderPath))
				yield break;

			var serializer = new XmlSerializer(typeof(Settings));

			foreach (var filePath in Directory.EnumerateFiles(folderPath, GetSettingsFileName("?")))
			{
				if (!TryLoad(filePath, serializer, out Settings instance))
					continue;

				if (int.TryParse(Path.GetFileNameWithoutExtension(filePath).LastOrDefault().ToString(), out int index))
					instance.Index = index;

				instance.LastWriteTime = GetLastWriteTime(filePath);

				yield return instance;
			}

			static DateTime GetLastWriteTime(string filePath)
			{
				try
				{
					return File.GetLastWriteTime(filePath);
				}
				catch
				{
					return default;
				}
			}
		}

		public static void Save(Settings instance) => Save(GetSettingsFilePath(instance.IndexString), instance);

		private static void Load<T>(in string filePath, T instance)
		{
			var fileInfo = new FileInfo(filePath);
			if (!fileInfo.Exists || (fileInfo.Length == 0))
				return;

			var serializer = new XmlSerializer(typeof(T));

			if (!TryLoad(filePath, serializer, out T buffer))
				return;

			typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Where(x => x.CanWrite)
				.ToList()
				.ForEach(x => x.SetValue(instance, x.GetValue(buffer)));
		}

		private static bool TryLoad<T>(in string filePath, XmlSerializer serializer, out T instance)
		{
			try
			{
				using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
				{
					instance = (T)serializer.Deserialize(fs);
					return true;
				}
			}
			catch (Exception ex)
			{
				FolderService.Delete(filePath);

				Debug.WriteLine($"Failed to load settings.\r\n{ex}");
				instance = default;
				return false;
			}
		}

		private static void Save<T>(in string filePath, T instance)
		{
			try
			{
				FolderService.AssureAppDataFolder();

				using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
				{
					var serializer = new XmlSerializer(typeof(T));
					serializer.Serialize(fs, instance);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to save settings.\r\n{ex}");
			}
		}

		internal static void Delete(Settings settings) => FolderService.Delete(GetSettingsFilePath(settings.IndexString));

		#endregion
	}
}