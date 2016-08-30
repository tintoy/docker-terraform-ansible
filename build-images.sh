#!/bin/bash

docker build -t netcore-docker-api -f docker-build/DockerExecutorApi/Dockerfile .
docker build -t ddresearch/terraform-ansible-deploy docker-build/Deployer
