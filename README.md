# HubSpot to Freshworks CRM Migration Script
This C# script automates the process of migrating contacts from HubSpot to Freshworks CRM. It uses the HubSpot API to fetch contacts and the Freshworks CRM API to create corresponding entries.

## Features
<ul>
  <li>Migrates contact information from HubSpot to Freshworks CRM</li>
  <li>Implements rate limiting for both HubSpot and Freshworks CRM APIs</li>
  <li>Handles API errors and implements retry logic</li>
  <li>Logs migration progress, failed contacts, and migrated contacts</li>
  <li>Supports resuming migration from the last processed contact</li>
</ul>

## Prerequisites
<ul>
  <li>.NET Core SDK (version 6.0)</li>
  <li>HubSpot API Bearer Token</li>
  <li>Freshworks CRM API Key</li>
  <li>NuGet packages:
    <ul>
      <li>Newtonsoft.Json</li>
      <li>RestSharp</li>
    </ul>
  </li>
</ul>


Configuration
Before running the script, you need to set up the following constants in the Program class:

HubSpotApiBaseUrl: The base URL for HubSpot API (usually "https://api.hubapi.com")
FreshworksCRMBaseUrl: Your Freshworks CRM base URL
HubSpotBearerToken: Your HubSpot API Bearer Token
FreshworksCRMApiKey: Your Freshworks CRM API Key

Usage

Clone the repository or download the script.
Open the solution in Visual Studio or your preferred C# IDE.
Install the required NuGet packages (Newtonsoft.Json and RestSharp).
Update the configuration constants with your API credentials.
Build and run the script.

The script will start migrating contacts from HubSpot to Freshworks CRM. It will display progress in the console and create log files for failed and migrated contacts.
How It Works

The script starts by loading the progress of any previous migration attempt.
It fetches contacts from HubSpot in batches of 100.
For each contact:

It creates a corresponding contact in Freshworks CRM.
If successful, it logs the migrated contact and updates the progress.
If unsuccessful, it logs the failed contact.


The script implements rate limiting to respect API usage limits:

100 requests per 10 seconds for HubSpot
40 requests per minute for Freshworks CRM


If an API call fails, the script will retry up to 3 times with exponential backoff.
The migration can be resumed from the last processed contact if interrupted.

Output Files
The script generates several output files:

migration_progress.json: Stores the ID of the last successfully processed contact.
failed_contacts.csv: Lists contacts that failed to migrate.
migrated_contacts.txt: Lists successfully migrated contacts.
api_error_log.txt: Logs detailed information about API errors.

Limitations

The script currently only migrates basic contact information. It does not handle custom fields or related records like deals or companies.
It does not migrate engagement history (emails, notes, tasks, etc.) associated with contacts.
The script does not handle pagination for HubSpot contacts beyond the initial 100 contacts.

Error Handling
The script implements several error handling mechanisms:

API errors are logged with detailed request and response information.
Failed contacts are logged separately for easy retry.
The script can be safely interrupted and resumed from the last successful contact migration.

Contributing
Contributions to improve the script or extend its functionality are welcome. Please feel free to submit issues or pull requests.
Disclaimer
This script is provided as-is. Make sure to test it thoroughly with a small dataset before running it on your entire database. Always ensure you have backups of your data before performing any migration.
License
MIT License
