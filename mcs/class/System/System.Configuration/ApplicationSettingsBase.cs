// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Copyright (C) 2005 Novell, Inc (http://www.novell.com)
//

#if NET_2_0
using System.Configuration.Provider;
using System.ComponentModel;
using System.Reflection;
using System.Collections.Specialized;

namespace System.Configuration {

        public abstract class ApplicationSettingsBase : SettingsBase, INotifyPropertyChanged
	{

                protected ApplicationSettingsBase ()
                {
			Initialize (Context, Properties, Providers);
                }

		protected ApplicationSettingsBase (IComponent owner)
			: this (owner, String.Empty)
		{
		}

 
		protected ApplicationSettingsBase (string settingsKey)
		{
			this.settingsKey = settingsKey;

			Initialize (Context, Properties, Providers);
		}

		protected ApplicationSettingsBase (IComponent owner, 
						   string settingsKey)
		{
			if (owner == null)
				throw new ArgumentNullException ();

			providerService = (ISettingsProviderService)owner.Site.GetService(typeof (ISettingsProviderService));

			this.settingsKey = settingsKey;

			Initialize (Context, Properties, Providers);
		}

		public event PropertyChangedEventHandler PropertyChanged;
		public event SettingChangingEventHandler SettingChanging;
		public event SettingsLoadedEventHandler SettingsLoaded;
		public event SettingsSavingEventHandler SettingsSaving;

		public object GetPreviousVersion (string propertyName)
		{
			throw new NotImplementedException ();
		}

		public void Reload ()
		{
			foreach (SettingsProvider provider in Providers) {
				IApplicationSettingsProvider iasp = provider as IApplicationSettingsProvider;
				if (iasp != null)
					iasp.Reset (Context);
			}
		}

		public void Reset()
		{
			foreach (SettingsProvider provider in Providers) {
				IApplicationSettingsProvider iasp = provider as IApplicationSettingsProvider;
				if (iasp != null)
					iasp.Reset (Context);
			}

			Reload ();
		}

		public override void Save()
		{
			/* ew.. this needs to be more efficient */
			foreach (SettingsProvider provider in Providers) {
				SettingsPropertyValueCollection cache = new SettingsPropertyValueCollection ();

				foreach (SettingsPropertyValue val in PropertyValues) {
					if (val.Property.Provider == provider)
						cache.Add (val);
				}

				if (cache.Count > 0)
					provider.SetPropertyValues (Context, cache);
			}
		}

		public virtual void Upgrade()
		{
		}

		protected virtual void OnPropertyChanged (object sender, 
							  PropertyChangedEventArgs e)
		{
			if (PropertyChanged != null)
				PropertyChanged (sender, e);
		}

		protected virtual void OnSettingChanging (object sender, 
							  SettingChangingEventArgs e)
		{
			if (SettingChanging != null)
				SettingChanging (sender, e);
		}

		protected virtual void OnSettingsLoaded (object sender, 
							 SettingsLoadedEventArgs e)
		{
			if (SettingsLoaded != null)
				SettingsLoaded (sender, e);
		}

		protected virtual void OnSettingsSaving (object sender, 
							 CancelEventArgs e)
		{
			if (SettingsSaving != null)
				SettingsSaving (sender, e);
		}

		[Browsable (false)]
		public override SettingsContext Context {
			get {
				if (context == null)
					context = new SettingsContext ();

				return context;
			}
		}

		void CacheValuesByProvider (SettingsProvider provider)
		{
			SettingsPropertyCollection col = new SettingsPropertyCollection ();

			foreach (SettingsProperty p in Properties) {
				if (p.Provider == provider)
					col.Add (p);
			}

			if (col.Count > 0) {
				SettingsPropertyValueCollection vals = provider.GetPropertyValues (Context, col);
				PropertyValues.Add (vals);
			}
		}

		void InitializeSettings (SettingsPropertyCollection settings)
		{
		}

		object GetPropertyValue (string propertyName)
		{
			SettingsProperty prop = Properties [ propertyName ];

			if (prop == null)
				throw new SettingsPropertyNotFoundException (propertyName);

			if (propertyValues == null)
				InitializeSettings (Properties);

			if (PropertyValues [ propertyName ] == null)
				CacheValuesByProvider (prop.Provider);

			return PropertyValues [ propertyName ].PropertyValue;
		}

		[MonoTODO]
		public override object this [ string propertyName ] {
			get {
				return GetPropertyValue (propertyName);
			}
			set {
				SettingsProperty prop = Properties [ propertyName ];

				if (prop == null)
					throw new SettingsPropertyNotFoundException (propertyName);

				if (prop.IsReadOnly)
					throw new SettingsPropertyIsReadOnlyException (propertyName);

				/* XXX check the type of the property vs the type of @value */
				if (value != null &&
				    !prop.PropertyType.IsAssignableFrom (value.GetType()))
					throw new SettingsPropertyWrongTypeException (propertyName);

				if (PropertyValues [ propertyName ] == null)
					CacheValuesByProvider (prop.Provider);

				SettingChangingEventArgs changing_args = new SettingChangingEventArgs (propertyName,
												       "" /* XXX settingClass? */,
												       settingsKey,
												       value,
												       false);

				OnSettingChanging (this, changing_args);

				if (changing_args.Cancel == false) {
					/* actually set the value */
					PropertyValues [ propertyName ].PropertyValue = value;

					/* then emit PropertyChanged */
					OnPropertyChanged (this, new PropertyChangedEventArgs (propertyName));
				}
			}
		}

		[Browsable (false)]
		public override SettingsPropertyCollection Properties {
			get {
				if (properties == null) {
					LocalFileSettingsProvider local_provider = null;

					properties = new SettingsPropertyCollection ();

					foreach (PropertyInfo prop in GetType().GetProperties (/* only public properties? */)) {
						SettingAttribute[] setting_attrs = (SettingAttribute[])prop.GetCustomAttributes (typeof (SettingAttribute), false);
						if (setting_attrs != null && setting_attrs.Length > 0 &&
						    (setting_attrs[0] is UserScopedSettingAttribute
						    || setting_attrs[0] is ApplicationScopedSettingAttribute)) {

							SettingsAttributeDictionary dict = new SettingsAttributeDictionary ();
							SettingsProvider provider = null;
							object defaultValue = null;
							SettingsSerializeAs serializeAs = SettingsSerializeAs.String;

							foreach (Attribute a in prop.GetCustomAttributes (false)) {
								if (a is UserScopedSettingAttribute
								    || a is ApplicationScopedSettingAttribute)
									continue;

								/* the attributes we handle natively here */
								if (a is SettingsProviderAttribute) {
									Type provider_type = Type.GetType (((SettingsProviderAttribute)a).ProviderTypeName);
									provider = (SettingsProvider) Activator.CreateInstance (provider_type);
								}
								else if (a is DefaultSettingValueAttribute) {
									defaultValue = ((DefaultSettingValueAttribute)a).Value; /* XXX this is a string.. do we convert? */
								}
								else if (a is SettingsSerializeAsAttribute) {
									serializeAs = ((SettingsSerializeAsAttribute)a).SerializeAs;
								}
								else {
									dict.Add (a.GetType().ToString() /* XXX ?*/, a);
								}
							}

							SettingsProperty setting = new SettingsProperty (prop.Name,
													 prop.PropertyType,
													 provider,
													 false /* XXX */,
													 defaultValue /* XXX always a string? */,
													 serializeAs,
													 dict,
													 false, false);


							if (providerService != null)
								setting.Provider = providerService.GetSettingsProvider (setting);

							if (provider == null) {
								if (local_provider == null)
									local_provider = new LocalFileSettingsProvider ();
								setting.Provider = local_provider;
							}

							if (provider != null) {
								/* make sure we're using the same instance of a
								   given provider across multiple properties */
								SettingsProvider p = Providers[provider.Name];
								if (p != null)
									setting.Provider = p;
							}

							properties.Add (setting);

							if (setting.Provider != null && Providers [setting.Provider.Name] == null)
								Providers.Add (setting.Provider);
						}
					}
				}

				return properties;
			}
		}

		[Browsable (false)]
		public override SettingsPropertyValueCollection PropertyValues {
			get {
				if (propertyValues == null) {
					propertyValues = new SettingsPropertyValueCollection ();
				}

				return propertyValues;
			}
		}

		[Browsable (false)]
		public override SettingsProviderCollection Providers {
			get {
				if (providers == null)
					providers = new SettingsProviderCollection ();

				return providers;
			}
		}

		[Browsable (false)]
		public string SettingsKey {
			get {
				return settingsKey;
			}
			set {
				settingsKey = value;
			}
		}

		string settingsKey;
		SettingsContext context;
		SettingsPropertyCollection properties;
		SettingsPropertyValueCollection propertyValues;
		SettingsProviderCollection providers;
		ISettingsProviderService providerService;
        }

}
#endif

