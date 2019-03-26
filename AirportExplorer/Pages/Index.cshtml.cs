using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using GoogleApi;
using GoogleApi.Entities.Common;
using GoogleApi.Entities.Common.Enums;
using GoogleApi.Entities.Places.Details.Request;
using GoogleApi.Entities.Places.Photos.Request;
using GoogleApi.Entities.Places.Search.NearBy.Request;
using MaxMind.GeoIP2;
using CsvHelper.Configuration;

namespace AirportExplorer.Pages
{
    public class IndexModel : PageModel
    {
        public string MapboxAccessToken { get; }
        public readonly IHostingEnvironment _hostingEnvironment;
        public string GoogleApiKey { get;}
        public double InitialLatitude { get; set; } = 0;
        public double InitialLongitude { get; set; } = 0;
        public int InitialZoom { get; set; } = 1;

        public IndexModel(IConfiguration configuration, IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
            MapboxAccessToken = configuration["Mapbox:AccessToken"];
            GoogleApiKey = configuration["google:ApiKey"];
        }
        public void OnGet()
        {
            try
            {
                using (var reader = new DatabaseReader(_hostingEnvironment.WebRootPath + "\\GeoLite2-City.mmdb"))
                {
                    // Determine the IP Address of the request
                    var ipAddress = HttpContext.Connection.RemoteIpAddress;
                    // Get the city from the IP Address
                    var city = reader.City(ipAddress);

                    if (city?.Location?.Latitude != null && city?.Location?.Longitude != null)
                    {
                        InitialLatitude = city.Location.Latitude.Value;
                        InitialLongitude = city.Location.Longitude.Value;
                        InitialZoom = 9;
                    }
                }
            }
            catch (Exception e)
            {
                // Just suppress errors. If we could not retrieve the location for whatever reason
                // there is not reason to notify the user. We'll just simply not know their current
                // location and won't be able to center the map on it
            }

        }
        public IActionResult OnGetAirports()
        {
            var configuration = new Configuration
            {
                BadDataFound = context =>
                {
                }
            };

            using (var sr = new StreamReader(Path.Combine(_hostingEnvironment.WebRootPath, "airports.dat")))
            
            using (var reader = new CsvReader(sr, configuration))
            {
                FeatureCollection featureCollection = new FeatureCollection();
                while (reader.Read())
                {
                    var name = reader.GetField<string>(1);
                    var iataCode = reader.GetField<string>(4);
                    var latitude = reader.GetField<double>(6);
                    var longitude = reader.GetField<double>(7);
                    featureCollection.Features.Add(
                        new Feature(new Point(new Position(latitude,longitude)),
                        new Dictionary<string, object>
                        {
                            {"name", name },
                            {"iataCode",iataCode }
                        }));
                }
                return new JsonResult(featureCollection);
            }          
        }
        public async Task<IActionResult> OnGetAirportDetail(string name,double latitude,
            double longitude)
        {
            var airportDetail = new AirportDetail();
            //Execute the search request
            var searchResponse = await GooglePlaces.NearBySearch.QueryAsync(new PlacesNearBySearchRequest
            {
                Key = GoogleApiKey,
                Name = name,
                Location = new Location(latitude, longitude),
                Radius = 1000

            });

            // If we did not get a good response, or the list of results are empty then get out of here
            if (!searchResponse.Status.HasValue || searchResponse.Status.Value != Status.Ok || !searchResponse.Results.Any())
                return new BadRequestResult();

            // Get the first result
            var nearbyResult = searchResponse.Results.FirstOrDefault();
            string placeId = nearbyResult.PlaceId;
            string photoReference = nearbyResult.Photos?.FirstOrDefault()?.PhotoReference;
            string photoCredit = nearbyResult.Photos?.FirstOrDefault()?.HtmlAttributions.FirstOrDefault();

            // Execute the details request
            var detailsResponse = await GooglePlaces.Details.QueryAsync(new PlacesDetailsRequest
            {
                Key = GoogleApiKey,
                PlaceId = placeId
            });

            // If we did not get a good response then get out of here
            if (!detailsResponse.Status.HasValue || detailsResponse.Status.Value != Status.Ok)
                return new BadRequestResult();

            // Set the details
            var detailsResult = detailsResponse.Result;
            airportDetail.FormattedAddress = detailsResult.FormattedAddress;
            airportDetail.PhoneNumber = detailsResult.InternationalPhoneNumber;
            airportDetail.Website = detailsResult.Website;

            if (photoReference != null)
            {
                // Execute the photo request
                var photosResponse = await GooglePlaces.Photos.QueryAsync(new PlacesPhotosRequest
                {
                    Key = GoogleApiKey,
                    PhotoReference = photoReference,
                    MaxWidth = 400
                });


                if (photosResponse.Buffer != null)
                {
                    airportDetail.Photo = Convert.ToBase64String(photosResponse.Buffer);
                    airportDetail.PhotoCredit = photoCredit;
                }
            }
            return new JsonResult(airportDetail);
        }
    }
}
