variable "region" {
  description = "AWS region"
  type        = string
  default     = "us-east-2"
}

variable "project" {
  description = "Project name used as prefix for all resources"
  type        = string
  default     = "healthmanager"
}

variable "db_instance_class" {
  description = "RDS instance class"
  type        = string
  default     = "db.t3.micro"
}

variable "jwt_secret" {
  description = "Secret key for JWT signing — minimum 32 characters, random string"
  type        = string
  sensitive   = true
}

variable "cors_origins" {
  description = "Comma-separated list of allowed CORS origins (e.g. https://app.example.com)"
  type        = string
}
