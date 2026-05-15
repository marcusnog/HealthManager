output "alb_dns_name" {
  description = "ALB DNS — use this as API_PROXY_TARGET in Amplify"
  value       = module.compute.alb_dns_name
}

output "ecr_api_url" {
  description = "ECR URL for API image — use in CI/CD"
  value       = module.compute.ecr_api_url
}

output "ecr_worker_url" {
  description = "ECR URL for Worker image — use in CI/CD"
  value       = module.compute.ecr_worker_url
}

output "ecs_cluster_name" {
  value = module.compute.ecs_cluster_name
}

output "ecs_api_service_name" {
  value = module.compute.ecs_api_service_name
}

output "ecs_worker_service_name" {
  value = module.compute.ecs_worker_service_name
}

output "rds_address" {
  description = "RDS endpoint — for debugging only, not exposed publicly"
  value       = module.database.db_address
}

output "documents_bucket_name" {
  value = module.storage.bucket_name
}
