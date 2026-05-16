terraform {
  required_version = ">= 1.7"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.0"
    }
    archive = {
      source  = "hashicorp/archive"
      version = "~> 2.0"
    }
  }

  backend "s3" {
    bucket       = "healthmanager-terraform-state-mn4821"
    key          = "production/terraform.tfstate"
    region       = "us-east-2"
    use_lockfile = true
    encrypt      = true
  }
}

provider "aws" {
  region = var.region

  default_tags {
    tags = {
      Project     = var.project
      Environment = "production"
      ManagedBy   = "terraform"
    }
  }
}

module "network" {
  source  = "./modules/network"
  project = var.project
}

module "database" {
  source             = "./modules/database"
  project            = var.project
  private_subnet_ids = module.network.private_subnet_ids
  db_sg_id           = module.network.db_sg_id
  db_instance_class  = var.db_instance_class
}

module "storage" {
  source      = "./modules/storage"
  bucket_name = "${var.project}-documents-prod"
}

module "compute" {
  source = "./modules/compute"

  project               = var.project
  vpc_id                = module.network.vpc_id
  public_subnet_ids     = module.network.public_subnet_ids
  private_subnet_ids    = module.network.private_subnet_ids
  alb_sg_id             = module.network.alb_sg_id
  api_sg_id             = module.network.api_sg_id
  worker_sg_id          = module.network.worker_sg_id
  documents_bucket_name = module.storage.bucket_name
  documents_bucket_arn  = module.storage.bucket_arn
  db_password_secret_arn = module.database.db_password_secret_arn
  database_url          = module.database.connection_string
  jwt_secret            = var.jwt_secret
  cors_origins          = var.cors_origins
}
