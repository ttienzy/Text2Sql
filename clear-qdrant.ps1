# Clear Qdrant Collection
# Use this to reset and force re-indexing

Write-Host "Clearing Qdrant collection 'schema_embeddings'..." -ForegroundColor Yellow

try {
    $response = Invoke-RestMethod -Uri "http://localhost:6333/collections/schema_embeddings" -Method Delete
    Write-Host "✓ Collection deleted successfully" -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 404) {
        Write-Host "⚠ Collection doesn't exist (already empty)" -ForegroundColor Yellow
    } else {
        Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Next time you run the app, schema will be re-indexed." -ForegroundColor Cyan
