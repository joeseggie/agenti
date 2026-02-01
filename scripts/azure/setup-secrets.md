# GitHub Secrets Setup Guide

This guide explains how to configure the required GitHub secrets for the Agenti CI/CD pipeline.

## Required Secrets

| Secret Name | Description |
|-------------|-------------|
| `AZURE_CREDENTIALS` | Service principal JSON for Azure login |
| `TEST_DB_PASSWORD` | Password for PostgreSQL test database in CI |

## Step-by-Step Setup

### 1. Create Test Database Password Secret

1. Go to your GitHub repository
2. Navigate to **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Name: `TEST_DB_PASSWORD`
5. Value: A secure password for the CI test database
6. Click **Add secret**

### 2. Create Azure Service Principal

Run the following command in Azure CLI (replace `<subscription-id>` with your actual subscription ID):

```bash
az ad sp create-for-rbac \
    --name "agenti-github-actions" \
    --role contributor \
    --scopes /subscriptions/<subscription-id>/resourceGroups/agenti-rg \
    --json-auth
```

This will output JSON similar to:

```json
{
  "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "clientSecret": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "subscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "activeDirectoryEndpointUrl": "https://login.microsoftonline.com",
  "resourceManagerEndpointUrl": "https://management.azure.com/",
  "activeDirectoryGraphResourceId": "https://graph.windows.net/",
  "sqlManagementEndpointUrl": "https://management.core.windows.net:8443/",
  "galleryEndpointUrl": "https://gallery.azure.com/",
  "managementEndpointUrl": "https://management.core.windows.net/"
}
```

### 3. Add Azure Credentials to GitHub

1. Go to your GitHub repository
2. Navigate to **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Name: `AZURE_CREDENTIALS`
5. Value: Paste the entire JSON output from the previous step
6. Click **Add secret**

### 4. Configure GitHub Environment (Optional but Recommended)

For additional security with the `production` environment:

1. Go to **Settings** → **Environments**
2. Click **New environment**
3. Name: `production`
4. Add protection rules:
   - **Required reviewers**: Add team members who must approve production deployments
   - **Wait timer**: Add a delay before deployment (optional)
5. Click **Save protection rules**

## Connection String

The connection string is configured directly in Azure App Service settings by the infrastructure script. It is **not** stored as a GitHub secret for security reasons.

If you need to update the connection string manually:

```bash
az webapp config connection-string set \
    --resource-group agenti-rg \
    --name agenti-web \
    --connection-string-type PostgreSQL \
    --settings DefaultConnection="Server=<postgres-ip>;Port=5432;Database=agenti_prod;User Id=agenti_user;Password=<password>;"
```

## Verifying Setup

After configuring secrets, you can verify by:

1. Push a commit to the `main` branch
2. Go to **Actions** tab in GitHub
3. Check the workflow run for any authentication errors

## Troubleshooting

### "Login failed with Error: Unable to get ACTIONS_ID_TOKEN_REQUEST_URL env variable"

This typically means the `AZURE_CREDENTIALS` secret is not set correctly. Verify:
- The secret name is exactly `AZURE_CREDENTIALS` (case-sensitive)
- The JSON is valid (no extra whitespace or characters)

### "The subscription '...' could not be found"

The service principal might not have access to the subscription. Run:

```bash
az role assignment list --assignee <client-id> --output table
```

### "Deployment failed - insufficient permissions"

The service principal needs Contributor role on the resource group. Re-run the `az ad sp create-for-rbac` command with the correct scope.

## Security Best Practices

1. **Rotate secrets regularly**: Delete and recreate the service principal every 90 days
2. **Use minimal permissions**: The service principal only has access to the `agenti-rg` resource group
3. **Enable branch protection**: Require PR reviews before merging to `main`
4. **Use environment protection rules**: Require approvals for production deployments
