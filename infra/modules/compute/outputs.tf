output "alb_dns_name" {
  value = aws_lb.main.dns_name
}

output "ecr_api_url" {
  value = aws_ecr_repository.api.repository_url
}

output "ecs_cluster_name" {
  value = aws_ecs_cluster.main.name
}

output "ecs_api_service_name" {
  value = aws_ecs_service.api.name
}

output "lambda_outbox_function_name" {
  value = aws_lambda_function.outbox.function_name
}
