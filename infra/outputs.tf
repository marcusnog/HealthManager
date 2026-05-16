output "alb_dns_name" {
  description = "ALB DNS — use this as API_PROXY_TARGET in Amplify"
  value       = module.compute.alb_dns_name
}

output "ecr_api_url" {
  description = "ECR URL for API image — use in CI/CD"
  value       = module.compute.ecr_api_url
}

output "ecs_cluster_name" {
  value = module.compute.ecs_cluster_name
}

output "ecs_api_service_name" {
  value = module.compute.ecs_api_service_name
}

output "lambda_outbox_function_name" {
  description = "Lambda function name for outbox processor — use in CI/CD"
  value       = module.compute.lambda_outbox_function_name
}

output "rds_address" {
  description = "RDS endpoint — for debugging only, not exposed publicly"
  value       = module.database.db_address
}

output "documents_bucket_name" {
  value = module.storage.bucket_name
}
