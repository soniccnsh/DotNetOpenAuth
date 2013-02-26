﻿//-----------------------------------------------------------------------
// <copyright file="TwitterConsumer.cs" company="Outercurve Foundation">
//     Copyright (c) Outercurve Foundation. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace DotNetOpenAuth.ApplicationBlock {
	using System;
	using System.Collections.Generic;
	using System.Configuration;
	using System.Globalization;
	using System.IO;
	using System.Net;
	using System.Net.Http;
	using System.Net.Http.Headers;
	using System.Runtime.Serialization.Json;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Web;
	using System.Xml;
	using System.Xml.Linq;
	using System.Xml.XPath;
	using DotNetOpenAuth.Messaging;
	using DotNetOpenAuth.Messaging.Bindings;
	using DotNetOpenAuth.OAuth;
	using DotNetOpenAuth.OAuth.ChannelElements;

	using Newtonsoft.Json.Linq;

	/// <summary>
	/// A consumer capable of communicating with Twitter.
	/// </summary>
	public static class TwitterConsumer {
		/// <summary>
		/// The description of Twitter's OAuth protocol URIs for use with actually reading/writing
		/// a user's private Twitter data.
		/// </summary>
		public static readonly ServiceProviderDescription ServiceDescription = new ServiceProviderDescription(
			"https://api.twitter.com/oauth/request_token",
			"https://api.twitter.com/oauth/authorize",
			"https://api.twitter.com/oauth/access_token");

		/// <summary>
		/// The description of Twitter's OAuth protocol URIs for use with their "Sign in with Twitter" feature.
		/// </summary>
		public static readonly ServiceProviderDescription SignInWithTwitterServiceDescription = new ServiceProviderDescription(
			"https://api.twitter.com/oauth/request_token",
			"https://api.twitter.com/oauth/authenticate",
			"https://api.twitter.com/oauth/access_token");

		/// <summary>
		/// The URI to get a user's favorites.
		/// </summary>
		private static readonly Uri GetFavoritesEndpoint = new Uri("http://twitter.com/favorites.xml");

		/// <summary>
		/// The URI to get the data on the user's home page.
		/// </summary>
		private static readonly Uri GetFriendTimelineStatusEndpoint = new Uri("https://api.twitter.com/1.1/statuses/home_timeline.json");

		private static readonly Uri UpdateProfileBackgroundImageEndpoint = new Uri("http://twitter.com/account/update_profile_background_image.xml");

		private static readonly Uri UpdateProfileImageEndpoint = new Uri("http://twitter.com/account/update_profile_image.xml");

		private static readonly Uri VerifyCredentialsEndpoint = new Uri("http://api.twitter.com/1/account/verify_credentials.xml");

		private class HostFactories : IHostFactories {
			private static readonly IHostFactories underlyingFactories = new DefaultOAuthHostFactories();

			public HttpMessageHandler CreateHttpMessageHandler() {
				return new WebRequestHandler();
			}

			public HttpClient CreateHttpClient(HttpMessageHandler handler = null) {
				var client = underlyingFactories.CreateHttpClient(handler);

				// Twitter can't handle the Expect 100 Continue HTTP header. 
				client.DefaultRequestHeaders.ExpectContinue = false;
				return client;
			}
		}

		public static Consumer CreateConsumer(bool forWeb = true) {
			string consumerKey = ConfigurationManager.AppSettings["twitterConsumerKey"];
			string consumerSecret = ConfigurationManager.AppSettings["twitterConsumerSecret"];
			if (IsTwitterConsumerConfigured) {
				ITemporaryCredentialStorage storage = forWeb ? (ITemporaryCredentialStorage)new CookieTemporaryCredentialStorage() : new MemoryTemporaryCredentialStorage();
				return new Consumer(consumerKey, consumerSecret, ServiceDescription, storage) {
					HostFactories = new HostFactories(),
				};
			} else {
				throw new InvalidOperationException("No Twitter OAuth consumer key and secret could be found in web.config AppSettings.");
			}
		}

		/// <summary>
		/// Gets a value indicating whether the Twitter consumer key and secret are set in the web.config file.
		/// </summary>
		public static bool IsTwitterConsumerConfigured {
			get {
				return !string.IsNullOrEmpty(ConfigurationManager.AppSettings["twitterConsumerKey"]) &&
					!string.IsNullOrEmpty(ConfigurationManager.AppSettings["twitterConsumerSecret"]);
			}
		}

		public static async Task<JArray> GetUpdatesAsync(Consumer twitter, AccessToken accessToken, CancellationToken cancellationToken = default(CancellationToken)) {
			using (var httpClient = twitter.CreateHttpClient(accessToken)) {
				using (var response = await httpClient.GetAsync(GetFriendTimelineStatusEndpoint, cancellationToken)) {
					response.EnsureSuccessStatusCode();
					string jsonString = await response.Content.ReadAsStringAsync();
					var json = JArray.Parse(jsonString);
					return json;
				}
			}
		}

		public static async Task<XDocument> GetFavorites(Consumer twitter, AccessToken accessToken, CancellationToken cancellationToken = default(CancellationToken)) {
			using (var httpClient = twitter.CreateHttpClient(accessToken)) {
				using (HttpResponseMessage response = await httpClient.GetAsync(GetFavoritesEndpoint, cancellationToken)) {
					response.EnsureSuccessStatusCode();
					return XDocument.Parse(await response.Content.ReadAsStringAsync());
				}
			}
		}

		public static async Task<XDocument> UpdateProfileBackgroundImageAsync(Consumer twitter, AccessToken accessToken, string image, bool tile, CancellationToken cancellationToken) {
			var imageAttachment = new StreamContent(File.OpenRead(image));
			imageAttachment.Headers.ContentType = new MediaTypeHeaderValue("image/" + Path.GetExtension(image).Substring(1).ToLowerInvariant());
			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, UpdateProfileBackgroundImageEndpoint);
			var content = new MultipartFormDataContent();
			content.Add(imageAttachment, "image");
			content.Add(new StringContent(tile.ToString().ToLowerInvariant()), "tile");
			request.Content = content;
			request.Headers.ExpectContinue = false;
			using (var httpClient = twitter.CreateHttpClient(accessToken)) {
				using (HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken)) {
					response.EnsureSuccessStatusCode();
					string responseString = await response.Content.ReadAsStringAsync();
					return XDocument.Parse(responseString);
				}
			}
		}

		public static Task<XDocument> UpdateProfileImageAsync(Consumer twitter, AccessToken accessToken, string pathToImage, CancellationToken cancellationToken = default(CancellationToken)) {
			string contentType = "image/" + Path.GetExtension(pathToImage).Substring(1).ToLowerInvariant();
			return UpdateProfileImageAsync(twitter, accessToken, File.OpenRead(pathToImage), contentType, cancellationToken);
		}

		public static async Task<XDocument> UpdateProfileImageAsync(Consumer twitter, AccessToken accessToken, Stream image, string contentType, CancellationToken cancellationToken = default(CancellationToken)) {
			var imageAttachment = new StreamContent(image);
			imageAttachment.Headers.ContentType = new MediaTypeHeaderValue(contentType);
			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, UpdateProfileImageEndpoint);
			var content = new MultipartFormDataContent();
			content.Add(imageAttachment, "image", "twitterPhoto");
			request.Content = content;
			using (var httpClient = twitter.CreateHttpClient(accessToken)) {
				using (HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken)) {
					response.EnsureSuccessStatusCode();
					string responseString = await response.Content.ReadAsStringAsync();
					return XDocument.Parse(responseString);
				}
			}
		}

		public static async Task<XDocument> VerifyCredentialsAsync(Consumer twitter, AccessToken accessToken, CancellationToken cancellationToken = default(CancellationToken)) {
			using (var httpClient = twitter.CreateHttpClient(accessToken)) {
				using (var response = await httpClient.GetAsync(VerifyCredentialsEndpoint, cancellationToken)) {
					response.EnsureSuccessStatusCode();
					using (var stream = await response.Content.ReadAsStreamAsync()) {
						return XDocument.Load(XmlReader.Create(stream));
					}
				}
			}
		}

		public static async Task<string> GetUsername(Consumer twitter, AccessToken accessToken, CancellationToken cancellationToken = default(CancellationToken)) {
			XDocument xml = await VerifyCredentialsAsync(twitter, accessToken, cancellationToken);
			XPathNavigator nav = xml.CreateNavigator();
			return nav.SelectSingleNode("/user/screen_name").Value;
		}

		/// <summary>
		/// Prepares a redirect that will send the user to Twitter to sign in.
		/// </summary>
		/// <param name="forceNewLogin">if set to <c>true</c> the user will be required to re-enter their Twitter credentials even if already logged in to Twitter.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <returns>
		/// The redirect message.
		/// </returns>
		/// <remarks>
		/// Call <see cref="OutgoingWebResponse.Send" /> or
		/// <c>return StartSignInWithTwitter().<see cref="MessagingUtilities.AsActionResult">AsActionResult()</see></c>
		/// to actually perform the redirect.
		/// </remarks>
		public static async Task<Uri> StartSignInWithTwitterAsync(bool forceNewLogin = false, CancellationToken cancellationToken = default(CancellationToken)) {
			var redirectParameters = new Dictionary<string, string>();
			if (forceNewLogin) {
				redirectParameters["force_login"] = "true";
			}
			Uri callback = MessagingUtilities.GetRequestUrlFromContext().StripQueryArgumentsWithPrefix("oauth_");

			var consumer = CreateConsumer();
			consumer.ServiceProvider = SignInWithTwitterServiceDescription;
			Uri redirectUrl = await consumer.RequestUserAuthorizationAsync(callback, cancellationToken: cancellationToken);
			return redirectUrl;
		}

		/// <summary>
		/// Checks the incoming web request to see if it carries a Twitter authentication response,
		/// and provides the user's Twitter screen name and unique id if available.
		/// </summary>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <returns>
		/// A tuple with the screen name and Twitter unique user ID if successful; otherwise <c>null</c>.
		/// </returns>
		public static async Task<Tuple<string, int>> TryFinishSignInWithTwitterAsync(Uri completeUrl, CancellationToken cancellationToken = default(CancellationToken)) {
			var consumer = CreateConsumer();
			consumer.ServiceProvider = SignInWithTwitterServiceDescription;
			var response = await consumer.ProcessUserAuthorizationAsync(completeUrl, cancellationToken: cancellationToken);
			if (response == null) {
				return null;
			}

			string screenName = response.ExtraData["screen_name"];
			int userId = int.Parse(response.ExtraData["user_id"]);
			return Tuple.Create(screenName, userId);
		}
	}
}
