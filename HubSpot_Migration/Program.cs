using Newtonsoft.Json;
using RestSharp;

namespace HubSpot_Migration
{
    internal class Program
    {
        private const string HubSpotApiBaseUrl = "https://api.hubapi.com";
        private const string FreshworksCRMBaseUrl = "FRESHWORKS_BASE_URL";
        private const string HubSpotBearerToken = "HubSpot_BEARER_TOKEN";
        private const string FreshworksCRMApiKey = "FRESHWORKS_CRM_API_KEY";

        private static readonly RestClient hubSpotClient = new RestClient(HubSpotApiBaseUrl);
        private static readonly RestClient freshworksCRMClient = new RestClient(FreshworksCRMBaseUrl);

        // Rate limiting settings
        private static readonly RateLimiter hubSpotRateLimiter = new RateLimiter(96, TimeSpan.FromSeconds(10));
        private static readonly RateLimiter freshworksCRMRateLimiter = new RateLimiter(40, TimeSpan.FromMinutes(1));

        private const string ProgressFilePath = "migration_progress.json";
        private const string FailedContactsFilePath = "failed_contacts.csv";
        private const string MigratedContactsFilePath = "migrated_contacts.txt";
        private const string ErrorLogFilePath = "api_error_log.txt";

        private const int MaxRetries = 3;

        private static int failedContactsCount = 0;
        private static int migratedContactsCount = 0;

        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("Starting migration process...");

                var progress = LoadProgress();
                var hubSpotContacts = await GetHubSpotContacts(progress.LastProcessedContactId);
                Console.WriteLine($"Found {hubSpotContacts.Count} contacts in HubSpot to process.");

                foreach (var contact in hubSpotContacts)
                {
                    try
                    {
                        await MigrateContactToFreshworksCRM(contact);
                        migratedContactsCount++;
                        LogMigratedContact(contact);

                        progress.LastProcessedContactId = contact.Id;
                        SaveProgress(progress);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to migrate contact {contact.Id}, {contact.Properties.FirstName} {contact.Properties.LastName}, {contact.Properties.Email}: {ex.Message}");
                        LogFailedContact(contact);

                        failedContactsCount++;
                    }
                }

                Console.WriteLine("Migration completed!");
                Console.WriteLine($"Total migrated contacts: {migratedContactsCount}");
                Console.WriteLine($"Total failed contacts: {failedContactsCount}");
                Console.WriteLine($"Failed contacts have been logged to {FailedContactsFilePath}");
                Console.WriteLine($"Migrated contacts have been logged to {MigratedContactsFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during migration: {ex.Message}");
                Console.WriteLine("You can resume the migration by running the program again.");
            }
        }

        private static async Task<List<HubSpotContact>> GetHubSpotContacts(string afterContactId = null)
        {
            var contacts = new List<HubSpotContact>();
            var hasMore = true;
            var after = afterContactId;

            while (hasMore)
            {
                await hubSpotRateLimiter.WaitForToken();

                var request = new RestRequest("/crm/v3/objects/contacts", Method.GET);
                request.AddHeader("Authorization", $"Bearer {HubSpotBearerToken}");
                request.AddParameter("limit", "100");
                request.AddParameter("properties", "firstname,lastname,email,phone,jobtitle,company,website,address,city,state,zip");
                if (!string.IsNullOrEmpty(after))
                {
                    request.AddParameter("after", after);
                }

                var response = await ExecuteWithRetry(() => hubSpotClient.ExecuteAsync(request));
                if (response.IsSuccessful)
                {
                    var result = JsonConvert.DeserializeObject<HubSpotContactsResponse>(response.Content);
                    contacts.AddRange(result.Results);
                    hasMore = result.Paging?.Next?.After != null;
                    after = result.Paging?.Next?.After;
                }
                else
                {
                    Console.WriteLine($"Failed to fetch HubSpot contacts: {response.ErrorMessage}");
                    LogApiError("HubSpot API Error", request, response);
                    hasMore = false;
                }
            }

            return contacts;
        }

        private static async Task MigrateContactToFreshworksCRM(HubSpotContact hubSpotContact)
        {
            var freshworksCRMContact = await CreateFreshworksCRMContact(hubSpotContact);
        }
        private static async Task<FreshworksCRMContact> CreateFreshworksCRMContact(HubSpotContact hubSpotContact)
        {
            await freshworksCRMRateLimiter.WaitForToken();

            var request = new RestRequest("/api/contacts", Method.POST);
            request.AddHeader("Authorization", $"Token token={FreshworksCRMApiKey}");
            request.AddHeader("Content-Type", "application/json");
            request.AddJsonBody(new
            {
                contact = new
                {
                    first_name = hubSpotContact.Properties.FirstName,
                    last_name = hubSpotContact.Properties.LastName,
                    email = hubSpotContact.Properties.Email,
                    mobile_number = hubSpotContact.Properties.Phone,
                    job_title = hubSpotContact.Properties.JobTitle,
                    company = hubSpotContact.Properties.Company,
                    website = hubSpotContact.Properties.Website,
                    address = hubSpotContact.Properties.Address,
                    city = hubSpotContact.Properties.City,
                    state = hubSpotContact.Properties.State,
                    zipcode = hubSpotContact.Properties.Zip
                }
            });

            var response = await ExecuteWithRetry(() => freshworksCRMClient.ExecuteAsync(request));
            if (response.IsSuccessful)
            {
                var result = JsonConvert.DeserializeObject<FreshworksCRMContactResponse>(response.Content);
                Console.WriteLine($"Created Freshworks CRM contact: {result.Contact.Id}, {result.Contact.FirstName} {result.Contact.LastName}, {result.Contact.Email}");
                return result.Contact;
            }
            else
            {
                Console.WriteLine($"Failed to create Freshworks CRM contact: {response.ErrorMessage}");
                LogApiError("Freshworks CRM API Error", request, response);
                return null;
            }
        }

        private static void LogApiError(string apiName, RestRequest request, IRestResponse response)
        {
            var errorLogMessage = $@"
                ------ {apiName} Error ------
                Time: {DateTime.Now}
                Request: 
                    Method: {request.Method}
                    Resource: {request.Resource}
                    Parameters: {string.Join(", ", request.Parameters.Select(p => $"{p.Name}={p.Value}"))}
                    Body: {request.Body?.ToString()}
                Response:
                    Status Code: {response.StatusCode}
                    Error Content: {response.Content}
                    Error Message: {response.ErrorMessage}
                ------------------------------------";

            Console.WriteLine(errorLogMessage);

            // Log to file
            try
            {
                File.AppendAllText(ErrorLogFilePath, errorLogMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to error log: {ex.Message}");
            }
        }

        private static async Task<IRestResponse> ExecuteWithRetry(Func<Task<IRestResponse>> action)
        {
            int retries = 0;
            while (retries < MaxRetries)
            {
                try
                {
                    var response = await action();
                    if (response.IsSuccessful || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return response;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}. Retrying...");
                    LogExceptionToFile(ex, ErrorLogFilePath);
                }

                retries++;
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries))); // Exponential backoff
            }

            throw new Exception($"Operation failed after {MaxRetries} retries.");
        }

        private static void LogExceptionToFile(Exception ex, string filePath)
        {
            try
            {
                // Create a new StreamWriter if the file doesn't exist, otherwise append to it
                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    writer.WriteLine("-----------------------------------------------------------------------------");
                    writer.WriteLine($"Date: {DateTime.Now}");
                    writer.WriteLine($"Exception Type: {ex.GetType().FullName}");
                    writer.WriteLine($"Message: {ex.Message}");
                    writer.WriteLine($"StackTrace: {ex.StackTrace}");

                    // Check for inner exceptions
                    Exception innerException = ex.InnerException;
                    while (innerException != null)
                    {
                        writer.WriteLine("-------------------- Inner Exception --------------------");
                        writer.WriteLine($"Exception Type: {innerException.GetType().FullName}");
                        writer.WriteLine($"Message: {innerException.Message}");
                        writer.WriteLine($"StackTrace: {innerException.StackTrace}");
                        innerException = innerException.InnerException;
                    }
                }
            }
            catch (Exception logEx)
            {
                // Handle any exceptions that might occur during logging
                Console.WriteLine($"An error occurred while logging the exception: {logEx.Message}");
            }
        }

        private static MigrationProgress LoadProgress()
        {
            if (File.Exists(ProgressFilePath))
            {
                var json = File.ReadAllText(ProgressFilePath);
                return JsonConvert.DeserializeObject<MigrationProgress>(json);
            }
            return new MigrationProgress();
        }

        private static void SaveProgress(MigrationProgress progress)
        {
            var json = JsonConvert.SerializeObject(progress);
            File.WriteAllText(ProgressFilePath, json);
        }

        private static void LogFailedContact(HubSpotContact contact)
        {
            var csvLine = $"{contact.Properties.FirstName},{contact.Properties.LastName},{contact.Properties.Email},{contact.Id}";
            File.AppendAllText(FailedContactsFilePath, csvLine + Environment.NewLine);
        }

        private static void LogMigratedContact(HubSpotContact contact)
        {
            var csvLine = $"{contact.Properties.FirstName},{contact.Properties.LastName},{contact.Properties.Email},{contact.Id}";
            File.AppendAllText(MigratedContactsFilePath, csvLine + Environment.NewLine);
        }
    }

    public class HubSpotContact
    {
        public string Id { get; set; }
        public HubSpotContactProperties Properties { get; set; }
    }

    public class MigrationProgress
    {
        public string LastProcessedContactId { get; set; }
    }
    public class HubSpotContactProperties
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string JobTitle { get; set; }
        public string Company { get; set; }
        public string Website { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
    }
    public class HubSpotContactsResponse
    {
        public List<HubSpotContact> Results { get; set; }
        public HubSpotPaging Paging { get; set; }
    }

    public class HubSpotPaging
    {
        public HubSpotNext Next { get; set; }
    }

    public class HubSpotNext
    {
        public string After { get; set; }
    }
    public class FreshworksCRMContact
    {
        public long Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string MobileNumber { get; set; }
        public string JobTitle { get; set; }
    }

    public class FreshworksCRMContactResponse
    {
        public FreshworksCRMContact Contact { get; set; }
    }

    // Rate Limiter class
    public class RateLimiter
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxRequests;
        private readonly TimeSpan _interval;
        private readonly Queue<DateTimeOffset> _requestTimestamps = new Queue<DateTimeOffset>();

        public RateLimiter(int maxRequests, TimeSpan interval)
        {
            _maxRequests = maxRequests;
            _interval = interval;
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public async Task WaitForToken()
        {
            await _semaphore.WaitAsync();
            try
            {
                while (true)
                {
                    var now = DateTimeOffset.UtcNow;
                    while (_requestTimestamps.Count > 0 && now - _requestTimestamps.Peek() > _interval)
                    {
                        _requestTimestamps.Dequeue();
                    }

                    if (_requestTimestamps.Count < _maxRequests)
                    {
                        _requestTimestamps.Enqueue(now);
                        break;
                    }

                    var oldestTimestamp = _requestTimestamps.Peek();
                    var waitTime = oldestTimestamp + _interval - now;
                    if (waitTime > TimeSpan.Zero)
                    {
                        await Task.Delay(waitTime);
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}