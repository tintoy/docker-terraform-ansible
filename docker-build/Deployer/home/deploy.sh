#!/bin/bash

set -e

if [ ! -e deploy.tf ]; then
    echo "'/root/deploy.tf' is not present; deploy.sh cannot be run directly from this container image; create an image that includes a Terraform configuration file called deploy.tf."

    exit 1
fi

if [ ! -e deploy.yml ]; then
    echo "'/root/deploy.yml' is not present; deploy.sh cannot be run directly from this container image; create an image that includes an Ansible playbook called deploy.yml."

    exit 1
fi

if [ ! -z $TF_VARIABLES_FILE ]; then
    if [ ! -f $TF_VARIABLES_FILE ]; then
        echo "Terraform variable override '$TF_VARIABLES_FILE' specified, but not found."

        exit 1
    fi

    echo "Terraform variable override '$TF_VARIABLES_FILE' detected; will use variables from this file."
    cp $TF_VARIABLES_FILE ./terraform.tfvars
fi

# User infrastructure.
echo "Applying Terraform configuration..."
terraform apply -no-color -state=./state/terraform.tfstate

echo "Dumping Terraform outputs to './state/terraform.output.json'..."
terraform output -json -state=./state/terraform.tfstate > ./state/terraform.output.json

# Wait for deployed hosts to come up. 
echo "Waiting for hosts to become available..."
ansible-playbook playbooks/wait-for-hosts.yml

# User deployment.
echo "Performing Ansible deployment..."
ansible-playbook deploy.yml
